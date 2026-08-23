using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 探明通知。探明狀態本身是即時推導的（見 <see cref="ResearchDiscoveryUtility"/>），
    /// 本元件只負責「哪些專案已經通知過玩家」這一項存檔狀態，避免重複發信。
    ///
    /// 種子化：首次啟用（新開局，或舊存檔第一次載入含本機制的版本）時，把當下已探明的
    /// 專案靜默記錄下來，否則開局瞬間會灌出一整串「已探明」信件。
    /// </summary>
    public class GameComponent_ResearchDiscovery : GameComponent
    {
        // 探明在畫面上是即時生效的，只有信件會延遲；10 秒掃一次已足夠，且成本可忽略。
        private const int CheckIntervalTicks = 600;

        private HashSet<ResearchProjectDef> announced = new HashSet<ResearchProjectDef>();
        private bool seeded;

        // 不存檔：讀檔後為 0，等於下一 tick 立刻補掃一次。
        private int ticksUntilCheck;

        // 掃描用暫存，避免每次配置新 List。
        private static readonly List<ResearchProjectDef> tmpNewlyDiscovered = new List<ResearchProjectDef>();

        public GameComponent_ResearchDiscovery(Game game) { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            EnsureSet();
            if (!seeded)
            {
                Scan(sendLetter: false);
                seeded = true;
            }
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (ticksUntilCheck > 0)
            {
                ticksUntilCheck--;
                return;
            }
            ticksUntilCheck = CheckIntervalTicks;
            Scan(sendLetter: true);
        }

        /// <summary>
        /// 掃描所有受管理的專案，記錄新探明者；<paramref name="sendLetter"/> 為 true 時
        /// 把這批新專案合併成單一封信件（一次完成前置往往同時解鎖數個專案）。
        /// </summary>
        private void Scan(bool sendLetter)
        {
            try
            {
                EnsureSet();
                List<ResearchProjectDef> managed = ResearchDiscoveryUtility.ManagedProjects;
                if (managed == null || managed.Count == 0)
                {
                    return;
                }

                tmpNewlyDiscovered.Clear();
                for (int i = 0; i < managed.Count; i++)
                {
                    ResearchProjectDef proj = managed[i];
                    if (proj == null || announced.Contains(proj))
                    {
                        continue;
                    }
                    if (ResearchDiscoveryUtility.IsUndiscovered(proj))
                    {
                        continue;
                    }
                    announced.Add(proj);

                    ModExtension_ResearchDiscovery ext = ResearchDiscoveryUtility.ExtensionFor(proj);
                    if (sendLetter && ext != null && ext.discoveryLetter)
                    {
                        tmpNewlyDiscovered.Add(proj);
                    }
                }

                if (tmpNewlyDiscovered.Count > 0)
                {
                    SendDiscoveryLetter(tmpNewlyDiscovered);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[FFF] GameComponent_ResearchDiscovery.Scan failed: {ex}");
            }
            finally
            {
                tmpNewlyDiscovered.Clear();
            }
        }

        private static void SendDiscoveryLetter(List<ResearchProjectDef> projects)
        {
            if (Find.LetterStack == null || projects == null || projects.Count == 0)
            {
                return;
            }

            // 取第一個有指定 letterDef 的專案；都沒指定就用中性的正面事件信。
            LetterDef letterDef = null;
            for (int i = 0; i < projects.Count && letterDef == null; i++)
            {
                ModExtension_ResearchDiscovery ext = ResearchDiscoveryUtility.ExtensionFor(projects[i]);
                if (ext != null)
                {
                    letterDef = ext.letterDef;
                }
            }
            if (letterDef == null)
            {
                letterDef = LetterDefOf.PositiveEvent;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("FFF_Research_DiscoveredLetterText".Translate());
            sb.AppendLine();
            for (int i = 0; i < projects.Count; i++)
            {
                sb.AppendLine("  - " + projects[i].LabelCap.ToString());
            }

            Find.LetterStack.ReceiveLetter(
                "FFF_Research_DiscoveredLetterLabel".Translate(),
                sb.ToString().TrimEndNewlines(),
                letterDef);
        }

        private void EnsureSet()
        {
            if (announced == null)
            {
                announced = new HashSet<ResearchProjectDef>();
            }
            // 存檔中已被移除的 Def 會還原成 null，清掉以免污染後續判斷。
            announced.RemoveWhere(p => p == null);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref announced, "fff_researchDiscoveryAnnounced", LookMode.Def);
            Scribe_Values.Look(ref seeded, "fff_researchDiscoverySeeded", defaultValue: false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureSet();
            }
        }
    }
}
