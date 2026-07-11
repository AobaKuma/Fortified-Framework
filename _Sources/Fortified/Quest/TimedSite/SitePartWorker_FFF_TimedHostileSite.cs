using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;
using Verse.Grammar;

namespace Fortified
{
    /// <summary>
    /// 通用限時敵對站點的 SitePartWorker：
    /// - 依威脅點數決定守軍與戰利品價值
    /// - 玩家進入地圖後啟動「敵方增援」倒數（難度越高越短），
    ///   細部參數由 SitePartDef 上的 FFF_TimedSiteExtension 設定
    /// Generic timed hostile site worker: threat points drive defenders & loot
    /// value; entering the map starts a hostile-reinforcement countdown that is
    /// shorter on higher difficulties. Tuned via FFF_TimedSiteExtension on the
    /// SitePartDef.
    /// </summary>
    public class SitePartWorker_FFF_TimedHostileSite : SitePartWorker
    {
        private static readonly FFF_TimedSiteExtension DefaultExtension = new FFF_TimedSiteExtension();

        private static readonly SimpleCurve ThreatPointsLootMarketValue = new SimpleCurve
        {
            new CurvePoint(100f, 300f),
            new CurvePoint(800f, 1200f),
            new CurvePoint(10000f, 2500f)
        };

        private FFF_TimedSiteExtension Extension => def.GetModExtension<FFF_TimedSiteExtension>() ?? DefaultExtension;

        public override SitePartParams GenerateDefaultParams(float myThreatPoints, PlanetTile tile, Faction faction)
        {
            SitePartParams parms = base.GenerateDefaultParams(myThreatPoints, tile, faction);
            if (faction != null)
            {
                parms.threatPoints = Mathf.Max(parms.threatPoints, faction.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat));
            }
            parms.lootMarketValue = ThreatPointsLootMarketValue.Evaluate(parms.threatPoints);
            return parms;
        }

        public override void Notify_GeneratedByQuestGen(SitePart part, Slate slate, List<Rule> outExtraDescriptionRules, Dictionary<string, string> outExtraDescriptionConstants)
        {
            base.Notify_GeneratedByQuestGen(part, slate, outExtraDescriptionRules, outExtraDescriptionConstants);
            int enemiesCount = GetEnemiesCount(part.site, part.parms);
            outExtraDescriptionRules.Add(new Rule_String("enemiesCount", enemiesCount.ToString()));
            outExtraDescriptionRules.Add(new Rule_String("enemiesLabel", GetEnemiesLabel(part.site, enemiesCount)));
        }

        public override string GetPostProcessedThreatLabel(Site site, SitePart sitePart)
        {
            if (site.Faction != null && site.Faction.IsPlayer) return null;
            return base.GetPostProcessedThreatLabel(site, sitePart) + ": "
                + "KnownSiteThreatEnemyCountAppend".Translate(GetEnemiesCount(site, sitePart.parms), "Enemies".Translate());
        }

        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);

            // 敵對站點：進入後有限時，逾時將引來敵方增援（之後會反覆增援）。
            // Hostile site: once entered, a countdown starts; when it expires,
            // enemy reinforcements arrive (and keep coming).
            FFF_TimedSiteExtension ext = Extension;
            int ticks = FFF_TimedSiteUtility.EntryCountdownTicks(ext.baseEntryHours, ext.minEntryHours, ext.maxEntryHours);
            TimedDetectionRaids timedRaids = map.Parent.GetComponent<TimedDetectionRaids>();
            if (timedRaids != null)
            {
                timedRaids.StartDetectionCountdown(ticks, 0);
                timedRaids.alertRaidsArrivingIn = true;
            }

            Find.LetterStack.ReceiveLetter(
                ext.entryLetterLabelKey.Translate(),
                ext.entryLetterTextKey.Translate(ticks.ToStringTicksToPeriod()),
                LetterDefOf.ThreatBig, map.Parent);
        }

        protected int GetEnemiesCount(Site site, SitePartParams parms)
        {
            return PawnGroupMakerUtility.GeneratePawnKindsExample(new PawnGroupMakerParms
            {
                tile = site.Tile,
                faction = site.Faction,
                groupKind = PawnGroupKindDefOf.Combat,
                points = parms.threatPoints,
                inhabitants = true,
                seed = OutpostSitePartUtility.GetPawnGroupMakerSeed(parms)
            }).Count();
        }

        protected string GetEnemiesLabel(Site site, int enemiesCount)
        {
            if (site.Faction == null)
            {
                return enemiesCount == 1 ? "Enemy".Translate() : "Enemies".Translate();
            }
            return enemiesCount == 1 ? site.Faction.def.pawnSingular : site.Faction.def.pawnsPlural;
        }
    }
}
