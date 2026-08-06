using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 「知識源」（石碑）的設定。內含有限的可萃取知識儲量；
    /// 由正面對準的萃取器 (<see cref="CompOccultechExtractor"/>) 逐步抽取。
    /// 本體不需要電力、不主動 tick，僅被動提供 <see cref="CompOccultechSource.TryExtract"/>。
    /// </summary>
    public class CompProperties_OccultechSource : CompProperties
    {
        /// <summary>本源可被萃取的知識點總量（耗盡後即成為空殼）。</summary>
        public float totalReserve = 4000f;

        /// <summary>
        /// 可選：限制本源只能被指定的知識類別萃取。留空 = 任何萃取器皆可抽取。
        /// 設定後，萃取器的 knowledgeCategory 必須與此相符才會運作。
        /// </summary>
        public KnowledgeCategoryDef requiredCategory;

        /// <summary>耗盡後是否自動摧毀本建築；false（預設）則保留為可拆除的空殼。</summary>
        public bool destroyOnDepleted = false;

        public CompProperties_OccultechSource()
        {
            compClass = typeof(CompOccultechSource);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
            {
                yield return e;
            }
            if (totalReserve <= 0f)
            {
                yield return $"{nameof(CompProperties_OccultechSource)}.totalReserve must be > 0 (got {totalReserve}).";
            }
        }
    }

    /// <summary>
    /// 有限知識源：提供 <see cref="TryExtract"/> 供萃取器抽取知識點，抽乾後保留為
    /// 可拆除的空殼（除非 destroyOnDepleted = true）。
    /// </summary>
    public class CompOccultechSource : ThingComp
    {
        // -1 代表尚未初始化，於 PostSpawnSetup 設為 totalReserve。
        private float reserveRemaining = -1f;

        public CompProperties_OccultechSource Props => (CompProperties_OccultechSource)props;

        public float ReserveRemaining => Mathf.Max(0f, reserveRemaining);

        public float TotalReserve => Props.totalReserve;

        public bool Depleted => reserveRemaining <= 0.0001f;

        /// <summary>剩餘比例 0~1，供 UI / 其他 mod 查詢。</summary>
        public float ReservePercent => TotalReserve <= 0f ? 0f : Mathf.Clamp01(ReserveRemaining / TotalReserve);

        /// <summary>本源是否接受指定類別的萃取器抽取。</summary>
        public bool AcceptsCategory(KnowledgeCategoryDef category)
        {
            return Props.requiredCategory == null || Props.requiredCategory == category;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (reserveRemaining < 0f)
            {
                reserveRemaining = Props.totalReserve;
            }
        }

        /// <summary>
        /// 嘗試抽取至多 <paramref name="requested"/> 點；回傳實際抽出的量（受剩餘儲量限制）。
        /// </summary>
        public float TryExtract(float requested)
        {
            if (requested <= 0f || Depleted)
            {
                return 0f;
            }
            float amount = Mathf.Min(requested, reserveRemaining);
            reserveRemaining -= amount;
            if (reserveRemaining < 0f)
            {
                reserveRemaining = 0f;
            }
            if (Depleted && Props.destroyOnDepleted && parent.Spawned && !parent.Destroyed)
            {
                parent.Destroy(DestroyMode.Vanish);
            }
            return amount;
        }

        /// <summary>外部（劇情事件、除錯工具等）補充儲量，上限為 totalReserve。</summary>
        public void Refill(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }
            reserveRemaining = Mathf.Min(TotalReserve, ReserveRemaining + amount);
        }

        public override string CompInspectStringExtra()
        {
            if (Depleted)
            {
                return "FFF_Occultech_SourceDepleted".Translate();
            }
            return "FFF_Occultech_SourceRemaining".Translate(
                ReserveRemaining.ToString("F0"),
                TotalReserve.ToString("F0"));
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            foreach (StatDrawEntry e in base.SpecialDisplayStats())
            {
                yield return e;
            }
            yield return new StatDrawEntry(
                StatCategoryDefOf.Building,
                "FFF_Occultech_SourceStatLabel".Translate(),
                ReserveRemaining.ToString("F0") + " / " + TotalReserve.ToString("F0"),
                "FFF_Occultech_SourceStatDesc".Translate(),
                1000);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
            if (!DebugSettings.ShowDevGizmos)
            {
                yield break;
            }
            yield return new Command_Action
            {
                defaultLabel = "DEV: Deplete reserve",
                action = () => reserveRemaining = 0f
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: Refill reserve",
                action = () => reserveRemaining = TotalReserve
            };
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref reserveRemaining, "reserveRemaining", -1f);
        }
    }
}
