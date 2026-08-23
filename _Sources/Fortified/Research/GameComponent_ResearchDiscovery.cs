using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 探明機制的存檔狀態。探明「條件」本身是即時推導的（見 <see cref="ResearchDiscoveryUtility"/>），
    /// 本元件只保存兩件無法推導的事：
    ///
    /// 1. <c>announced</c>：哪些專案已經通知過玩家，避免重複發信。
    /// 2. <c>forced</c>：哪些專案被外力（解封物品、事件、任務獎勵）**強制探明**。
    ///    強制探明會蓋過前置研究的推導結果，且永久有效，因此必須存檔。
    ///
    /// 種子化：首次啟用（新開局，或舊存檔第一次載入含本機制的版本）時，把當下已探明的
    /// 專案靜默記錄下來，否則開局瞬間會灌出一整串「已探明」信件。
    /// </summary>
    public class GameComponent_ResearchDiscovery : GameComponent
    {
        // 探明在畫面上是即時生效的，只有信件會延遲；10 秒掃一次已足夠，且成本可忽略。
        private const int CheckIntervalTicks = 600;

        private HashSet<ResearchProjectDef> announced = new HashSet<ResearchProjectDef>();

        // 被外力強制探明的專案。空集合是最常見的情況，故延後配置以免每局都背一個空 HashSet。
        private HashSet<ResearchProjectDef> forced;

        private bool seeded;

        // 不存檔：讀檔後為 0，等於下一 tick 立刻補掃一次。
        private int ticksUntilCheck;

        // 掃描用暫存，避免每次配置新 List。
        private static readonly List<ResearchProjectDef> tmpNewlyDiscovered = new List<ResearchProjectDef>();

        // CompSafe 會被 IsHidden（每 frame × 每專案）間接呼叫，不能每次都走 Game.GetComponent。
        // 連同 Game 實例一起快取，換存檔／回主選單時自動失效。
        private static Game cachedGame;
        private static GameComponent_ResearchDiscovery cached;

        public GameComponent_ResearchDiscovery(Game game) { }

        /// <summary>
        /// 取得目前遊戲的組件實例；尚未進入遊戲時回傳 null（不拋例外）。
        /// </summary>
        public static GameComponent_ResearchDiscovery CompSafe
        {
            get
            {
                Game game = Current.Game;
                if (game == null)
                {
                    // 回主選單時一併放掉舊實例，避免跨局殘留。
                    cachedGame = null;
                    cached = null;
                    return null;
                }
                if (!ReferenceEquals(game, cachedGame))
                {
                    cachedGame = game;
                    cached = game.GetComponent<GameComponent_ResearchDiscovery>();
                }
                return cached;
            }
        }

        /// <summary>此專案是否已被外力強制探明。任何失敗都回傳 false（＝交回一般推導）。</summary>
        public static bool IsForceDiscoveredSafe(ResearchProjectDef proj)
        {
            if (proj == null)
            {
                return false;
            }
            try
            {
                GameComponent_ResearchDiscovery comp = CompSafe;
                return comp != null && comp.forced != null && comp.forced.Contains(proj);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 強制探明一個專案。回傳 true 表示這次呼叫真的改變了狀態（先前未被強制探明）。
        /// <paramref name="suppressLetter"/> 為 true 時同時標記為「已通知」，
        /// 讓呼叫方自行決定要怎麼告知玩家，不會再多出一封通用探明信。
        /// </summary>
        public bool ForceDiscover(ResearchProjectDef proj, bool suppressLetter)
        {
            if (proj == null)
            {
                return false;
            }
            EnsureSet();
            bool changed = forced.Add(proj);
            if (suppressLetter)
            {
                announced.Add(proj);
            }
            return changed;
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            cachedGame = Current.Game;
            cached = this;
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
            if (forced == null)
            {
                forced = new HashSet<ResearchProjectDef>();
            }
            // 存檔中已被移除的 Def 會還原成 null，清掉以免污染後續判斷。
            announced.RemoveWhere(p => p == null);
            forced.RemoveWhere(p => p == null);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref announced, "fff_researchDiscoveryAnnounced", LookMode.Def);
            Scribe_Collections.Look(ref forced, "fff_researchDiscoveryForced", LookMode.Def);
            Scribe_Values.Look(ref seeded, "fff_researchDiscoverySeeded", defaultValue: false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureSet();
            }
        }
    }
}
