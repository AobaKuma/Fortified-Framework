using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;
using Verse.Grammar;

namespace Fortified
{
    /// <summary>
    /// 通用限時敵對站點任務根節點：在世界地圖產生一個指定派系的敵對站點，
    /// 附帶依難度縮放的逾期時限；清除全部敵人即成功。
    /// Generic root quest node: spawns a hostile site of the given faction on
    /// the world map with a difficulty-scaled expiry; clearing all enemies
    /// completes the quest.
    /// </summary>
    public class QuestNode_Root_FFF_TimedHostileSite : QuestNode
    {
        public FactionDef factionDef;
        public SitePartDef sitePartDef;

        // 逾期回退值（slate 沒有 "timeoutTicks" 時使用，例如除錯生成）。
        // Fallbacks used when the slate has no "timeoutTicks" (e.g. debug spawn).
        public float baseTimeoutDays = 16f;
        public float minTimeoutDays = 4f;
        public float maxTimeoutDays = 30f;

        // 信件翻譯鍵，可由 XML 覆寫。Letter translation keys, overridable from XML.
        [NoTranslate] public string expiredLetterLabelKey = "FFF_TimedSite_ExpiredLabel";
        [NoTranslate] public string expiredLetterTextKey = "FFF_TimedSite_ExpiredText";
        [NoTranslate] public string clearedLetterLabelKey = "FFF_TimedSite_ClearedLabel";
        [NoTranslate] public string clearedLetterTextKey = "FFF_TimedSite_ClearedText";

        /// <summary>
        /// 生成站點前先跑原版 Util_AdjustPointsForDistantFight，把 slate 的襲擊點數壓成遠征用的點數
        /// （有重力引擎時壓得較少），守軍才會跟原版站點任務同一個量級。預設關閉，維持舊行為。
        /// 只在 slate 帶的是說書人原始襲擊點數時開啟：已經用 DefaultSiteThreatPointsNow 壓過的來源
        /// （CompUseEffect_GiveTimedSiteQuest、追殺的 siteThreatPoints）再開會被壓兩次。
        /// 戰利品價值仍照壓縮前的點數算，就像原版壓低威脅後會把酬勞放大回去。
        ///
        /// Runs vanilla's Util_AdjustPointsForDistantFight on the slate's raid points before the site is
        /// generated, so the garrison lands on the same scale as vanilla site quests (a grav engine softens the
        /// cut). Off by default: the old behaviour stands. Only turn it on where the slate carries raw storyteller
        /// raid points; sources that already pass DefaultSiteThreatPointsNow (CompUseEffect_GiveTimedSiteQuest,
        /// huntdowns with siteThreatPoints) would be cut twice. Loot value still follows the uncut points, the way
        /// vanilla inflates rewards back after deflating the threat.
        /// </summary>
        public bool adjustPointsForDistantFight;

        private const string DistantFightScript = "Util_AdjustPointsForDistantFight";

        protected override bool TestRunInt(Slate slate)
        {
            if (factionDef == null || sitePartDef == null) return false;
            if (Find.FactionManager.FirstFactionOfDef(factionDef) == null) return false;
            return TileFinder.TryFindNewSiteTile(out _, exitOnFirstTileFound: true);
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Quest quest = QuestGen.quest;

            Faction faction = Find.FactionManager.FirstFactionOfDef(factionDef);
            if (faction == null)
            {
                Log.Error("[FortifiedFramework] QuestNode_Root_FFF_TimedHostileSite: no faction of def " + factionDef?.defName);
                return;
            }

            float points = slate.Get("points", StorytellerUtility.DefaultSiteThreatPointsNow());
            float lootPoints = points;
            // 沒有 "points" 時的回退值本來就是站點點數，不再壓。The fallback is already site points; don't cut it again.
            if (adjustPointsForDistantFight && slate.Exists("points"))
            {
                points = AdjustedForDistantFight(slate, points);
            }
            int timeoutTicks = slate.Get("timeoutTicks", 0);
            if (timeoutTicks <= 0)
            {
                timeoutTicks = FFF_TimedSiteUtility.QuestTimeoutTicks(baseTimeoutDays, minTimeoutDays, maxTimeoutDays);
            }

            if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile))
            {
                Log.Error("[FortifiedFramework] QuestNode_Root_FFF_TimedHostileSite: failed to find a site tile.");
                return;
            }

            // 生成站點（threat points 交由 SitePartWorker 分配）。
            SiteMakerHelper.GenerateDefaultParams(points, tile, faction, Gen.YieldSingle(sitePartDef), out List<SitePartDefWithParams> partsParams);
            if (lootPoints > points)
            {
                foreach (SitePartDefWithParams part in partsParams)
                {
                    if (part.def.Worker is SitePartWorker_FFF_TimedHostileSite)
                    {
                        part.parms.lootMarketValue = SitePartWorker_FFF_TimedHostileSite.LootMarketValueFor(Mathf.Max(lootPoints, part.parms.threatPoints));
                    }
                }
            }
            Site site = QuestGen_Sites.GenerateSite(partsParams, tile, faction);
            slate.Set("site", site);
            QuestUtility.AddQuestTag(ref site.questTags, QuestGenUtility.HardcodedTargetQuestTagWithQuestID("site"));

            quest.SpawnWorldObject(site);

            // 逾期：時限一到站點消失、任務失敗。
            // Expiry: when the timer runs out the site vanishes and the quest fails.
            string expiredSignal = QuestGenUtility.HardcodedSignalWithQuestID("site.QuestTimeout");
            QuestPart_WorldObjectTimeout timeout = new QuestPart_WorldObjectTimeout
            {
                worldObject = site,
                delayTicks = timeoutTicks,
                inSignalEnable = quest.InitiateSignal,
                inSignalDisable = QuestGenUtility.HardcodedSignalWithQuestID("site.MapGenerated"),
                isBad = true,
                expiryInfoPart = "QuestExpiresIn".Translate(),
                expiryInfoPartTip = "QuestExpiresOn".Translate(),
                destroyOnCleanup = true
            };
            timeout.outSignalsCompleted.Add(expiredSignal);
            quest.AddPart(timeout);

            quest.Letter(LetterDefOf.NegativeEvent, inSignal: expiredSignal,
                label: expiredLetterLabelKey.Translate(),
                text: expiredLetterTextKey.Translate());
            quest.End(QuestEndOutcome.Fail, inSignal: expiredSignal);

            // 成功：清除站點所有敵人。
            // Success: all enemies at the site defeated.
            string clearedSignal = QuestGenUtility.HardcodedSignalWithQuestID("site.AllEnemiesDefeated");
            quest.Letter(LetterDefOf.PositiveEvent, inSignal: clearedSignal,
                label: clearedLetterLabelKey.Translate(),
                text: clearedLetterTextKey.Translate());
            quest.End(QuestEndOutcome.Success, inSignal: clearedSignal);

            // 玩家離開且地圖被移除：任務靜默結束。
            // Player left and the map got removed: end quietly.
            quest.End(QuestEndOutcome.Unknown, inSignal: QuestGenUtility.HardcodedSignalWithQuestID("site.MapRemoved"));

            // 提供給任務名稱／描述的規則。Rules for quest name/description text.
            QuestGen.AddQuestDescriptionRules(new List<Rule>
            {
                new Rule_String("siteTimeout", timeoutTicks.ToStringTicksToPeriod()),
                new Rule_String("siteFactionName", faction.Name)
            });
        }

        /// <summary>
        /// 跟 QuestNode_SubScript 一樣直接執行原版腳本，它會把壓縮後的值寫回 slate 的 "points"；
        /// 曲線因此跟著原版（以及修改它的模組）走，不在這裡複製一份。
        /// Runs the vanilla script the way QuestNode_SubScript does; it writes the cut value back into the slate's
        /// "points", so the curve stays vanilla's (and whatever patches it) instead of a copy kept here.
        /// </summary>
        private static float AdjustedForDistantFight(Slate slate, float points)
        {
            QuestScriptDef script = DefDatabase<QuestScriptDef>.GetNamedSilentFail(DistantFightScript);
            if (script == null)
            {
                Log.ErrorOnce("[FortifiedFramework] QuestNode_Root_FFF_TimedHostileSite: " + DistantFightScript + " is missing; site points left uncut.", 52817433);
                return points;
            }
            script.Run();
            return slate.Get("points", points);
        }
    }
}
