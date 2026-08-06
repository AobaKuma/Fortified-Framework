using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified
{
    public class CompProperties_OccultechExtractor : CompProperties
    {
        /// <summary>每遊戲日萃取（並注入研究）的知識點數。</summary>
        public float knowledgePerDay = 600f;

        /// <summary>知識注入的起始類別；溢流順序由該類別所屬研究分頁的類別排序決定。</summary>
        public KnowledgeCategoryDef knowledgeCategory;

        /// <summary>
        /// 搜尋知識源的方式。true（預設）＝只認正面對準的那一排格；
        /// false ＝掃描整圈相鄰格（含側面與背面）。
        /// </summary>
        public bool requireFacing = true;

        /// <summary>
        /// 需要電力才能運作。設為 false 時即使沒有 CompPowerTrader（或斷電）也持續運作。
        /// </summary>
        public bool requiresPower = true;

        public CompProperties_OccultechExtractor()
        {
            compClass = typeof(CompOccultechExtractor);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
            {
                yield return e;
            }
            if (knowledgePerDay <= 0f)
            {
                yield return $"{nameof(CompProperties_OccultechExtractor)}.knowledgePerDay must be > 0 (got {knowledgePerDay}).";
            }
            if (knowledgeCategory == null)
            {
                yield return $"{nameof(CompProperties_OccultechExtractor)}.knowledgeCategory is not set; the extractor would have nowhere to file its knowledge.";
            }
            if (parentDef != null && parentDef.tickerType != TickerType.Rare && parentDef.tickerType != TickerType.Normal)
            {
                yield return $"{nameof(CompProperties_OccultechExtractor)} requires tickerType Rare or Normal (got {parentDef.tickerType}).";
            }
        }
    }

    /// <summary>
    /// 自動萃取器：需要電力 + 對準未耗盡的知識源 + 該分頁尚有可推進的知識專案。
    /// 三者皆滿足時每 rare-tick 抽取知識點，透過
    /// <see cref="KnowledgeUtility.AddKnowledge"/> 注入起始類別（並依分頁順序向後溢流）。
    /// 任一條件不滿足即轉為待機，不抽取源的儲量、僅耗待機電力。
    /// </summary>
    public class CompOccultechExtractor : ThingComp
    {
        private CompPowerTrader powerComp;
        // 累積未滿 1 點的小數，避免每 tick 都做浮點注入。
        private float buffer;
        // 快取上一次找到的源，省去每 rare-tick 重掃鄰格；失效時自動重找。
        private CompOccultechSource cachedSource;

        public CompProperties_OccultechExtractor Props => (CompProperties_OccultechExtractor)props;

        private float KnowledgePerRareTick =>
            Props.knowledgePerDay * GenTicks.TickRareInterval / GenDate.TicksPerDay;

        private bool PowerOk => !Props.requiresPower || powerComp == null || powerComp.PowerOn;

        /// <summary>目前對準的知識源（可能已耗盡）；沒有則為 null。</summary>
        public CompOccultechSource CurrentSource => FindSource();

        /// <summary>本萃取器是否正在實際抽取。</summary>
        public bool Working
        {
            get
            {
                CompOccultechSource src = FindSource();
                return PowerOk && src != null && !src.Depleted
                       && KnowledgeUtility.HasResearchTarget(Props.knowledgeCategory);
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();
            cachedSource = null;
        }

        public override void PostDeSpawn(Map map, DestroyMode mode)
        {
            base.PostDeSpawn(map, mode);
            cachedSource = null;
        }

        /// <summary>快取有效性檢查：源仍存在、仍生成於同一地圖、且仍在有效範圍內。</summary>
        private bool CacheValid()
        {
            return cachedSource != null
                   && cachedSource.parent != null
                   && !cachedSource.parent.Destroyed
                   && cachedSource.parent.Spawned
                   && cachedSource.parent.Map == parent.Map;
        }

        private CompOccultechSource FindSource()
        {
            if (CacheValid())
            {
                return cachedSource;
            }
            cachedSource = ScanForSource();
            return cachedSource;
        }

        /// <summary>
        /// 掃描鄰接格尋找知識源。requireFacing = true 時只看本建築「正面」
        /// （面向方向 parent.Rotation）緊鄰的那一排格；否則掃描整圈相鄰格。
        /// 優先回傳未耗盡的源；找不到則回傳已耗盡者（供狀態顯示），皆無則 null。
        /// </summary>
        private CompOccultechSource ScanForSource()
        {
            if (!parent.Spawned || parent.Map == null)
            {
                return null;
            }

            Map map = parent.Map;
            CellRect rect = parent.OccupiedRect();
            CompOccultechSource fallback = null;

            foreach (IntVec3 cell in CandidateCells(rect))
            {
                if (rect.Contains(cell) || !cell.InBounds(map))
                {
                    continue;
                }
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    CompOccultechSource src = (things[i] as ThingWithComps)?.GetComp<CompOccultechSource>();
                    if (src == null || !src.AcceptsCategory(Props.knowledgeCategory))
                    {
                        continue;
                    }
                    if (!src.Depleted)
                    {
                        return src;
                    }
                    fallback = src; // 記住已耗盡的源，供狀態顯示用。
                }
            }
            return fallback;
        }

        /// <summary>依 requireFacing 產生待掃描的格子。</summary>
        private IEnumerable<IntVec3> CandidateCells(CellRect rect)
        {
            if (Props.requireFacing)
            {
                IntVec3 facing = parent.Rotation.FacingCell; // 正面單位方向
                foreach (IntVec3 cell in rect.Cells)
                {
                    yield return cell + facing;
                }
                yield break;
            }

            foreach (IntVec3 cell in rect.ExpandedBy(1).EdgeCells)
            {
                yield return cell;
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!parent.Spawned)
            {
                return;
            }

            CompOccultechSource source = FindSource();
            // 只有在「有電力 + 有未耗盡的源 + 尚有可推進的知識專案」時才運作。
            bool active = PowerOk && source != null && !source.Depleted
                          && KnowledgeUtility.HasResearchTarget(Props.knowledgeCategory);

            // 依運作狀態調整耗電（有源在抽=滿載，否則=待機）。
            if (powerComp != null)
            {
                if (active)
                {
                    powerComp.PowerOutput = -powerComp.Props.PowerConsumption;
                }
                else if (powerComp.Props.idlePowerDraw >= 0f)
                {
                    powerComp.PowerOutput = -powerComp.Props.idlePowerDraw;
                }
            }

            if (!active)
            {
                return;
            }

            buffer += KnowledgePerRareTick;
            if (buffer < 1f)
            {
                return;
            }

            float want = Mathf.Floor(buffer);
            float got = source.TryExtract(want);
            buffer -= want;

            if (got > 0f)
            {
                KnowledgeUtility.AddKnowledge(Props.knowledgeCategory, got);
            }
        }

        public override string CompInspectStringExtra()
        {
            if (!parent.Spawned)
            {
                return null;
            }

            if (!PowerOk)
            {
                return "FFF_Occultech_ExtractorNoPower".Translate();
            }

            CompOccultechSource source = FindSource();
            if (source == null)
            {
                return "FFF_Occultech_ExtractorNoSource".Translate();
            }
            if (source.Depleted)
            {
                return "FFF_Occultech_ExtractorSourceDepleted".Translate();
            }
            if (!KnowledgeUtility.HasResearchTarget(Props.knowledgeCategory))
            {
                return "FFF_Occultech_ExtractorNoResearch".Translate();
            }

            return "FFF_Occultech_ExtractorWorking".Translate(
                Props.knowledgePerDay.ToString("F0"),
                source.ReserveRemaining.ToString("F0"));
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            foreach (StatDrawEntry e in base.SpecialDisplayStats())
            {
                yield return e;
            }
            yield return new StatDrawEntry(
                StatCategoryDefOf.Building,
                "FFF_Occultech_ExtractorStatLabel".Translate(),
                Props.knowledgePerDay.ToString("F0"),
                "FFF_Occultech_ExtractorStatDesc".Translate(
                    Props.knowledgeCategory != null ? Props.knowledgeCategory.LabelCap.ToString() : "-"),
                999);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref buffer, "occultechExtractBuffer", 0f);
        }
    }
}
