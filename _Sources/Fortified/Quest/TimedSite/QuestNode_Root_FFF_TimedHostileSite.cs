using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
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
    }
}
