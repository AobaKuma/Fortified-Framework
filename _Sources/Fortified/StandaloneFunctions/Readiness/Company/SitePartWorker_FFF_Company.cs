using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;
using Verse.Grammar;

namespace Fortified;

/// <summary>
/// 連隊駐地的站點部件：定期對連隊下令；與玩家敵對時，會從駐地襲擊作戰半徑內的玩家據點。
/// Site part of a company camp: orders the company periodically and, when hostile to the player, raids player homes
/// within its combat radius.
/// </summary>
public class SitePartWorker_FFF_Company : SitePartWorker_Outpost
{
    // 原版 Site.Tick() 每個 world tick 都呼叫 SitePartWorkerTick，這裡必須自己節流。
    // Vanilla calls SitePartWorkerTick on every world tick, so throttle here.
    private const int TickInterval = 2500;

    public override void SitePartWorkerTick(SitePart sitePart)
    {
        base.SitePartWorkerTick(sitePart);
        if (sitePart?.site is not FFF_Company company || !company.IsHashIntervalTick(TickInterval)) return;
        CompanyDef def = company.CompanyDef;
        if (def == null) return;
        int cooldown = Mathf.RoundToInt(def.raidCooldownDays * GenDate.TicksPerDay);
        if (sitePart.lastRaidTick != -1 && Find.TickManager.TicksGame < sitePart.lastRaidTick + cooldown) return;

        // 先上膛：襲擊與下令共用同一個冷卻。Re-arm first: raids and orders share one cooldown.
        sitePart.lastRaidTick = Find.TickManager.TicksGame;
        if (!company.HasMap) TryRaidNearbyHomes(sitePart, company, def);
        company.IssueOrder();
    }

    protected virtual void TryRaidNearbyHomes(SitePart sitePart, FFF_Company company, CompanyDef def)
    {
        Faction faction = company.Faction;
        if (faction == null || !faction.HostileTo(Faction.OfPlayer)) return;
        float mtbDays = QuestTuning.PointsToRaidSourceRaidsMTBDaysCurve.Evaluate(sitePart.parms.threatPoints);
        foreach (Map map in Find.Maps.Where(m => m.IsPlayerHome).ToList())
        {
            if (Find.WorldGrid.ApproxDistanceInTiles(map.Tile, company.Tile) > def.combatRadius) continue;
            if (Rand.MTBEventOccurs(mtbDays, GenDate.TicksPerDay, TickInterval)) CompanyUtility.RaidMap(map, def, faction);
        }
    }

    public override void Init(Site site, SitePart sitePart)
    {
        base.Init(site, sitePart);
        sitePart.lastRaidTick = Find.TickManager.TicksGame;
    }

    public override void PostDrawExtraSelectionOverlays(SitePart sitePart)
    {
        base.PostDrawExtraSelectionOverlays(sitePart);
        if (sitePart.site is FFF_Company company && company.CompanyDef != null)
        {
            GenDraw.DrawWorldRadiusRing(company.Tile, company.CompanyDef.combatRadius);
        }
    }

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

    /// <summary>
    /// 估計的兵力。編制是隨機的，這裡取期望值而不是每次重擲（重擲會讓檢視面板的數字一直跳）。
    /// Estimated strength. The roster is random, so use its expectation rather than re-rolling (which made the
    /// inspect pane flicker).
    /// </summary>
    protected static int EstimatedPawnCount(Site site)
    {
        CompanyDef def = (site as FFF_Company)?.CompanyDef;
        if (def == null || def.squads.NullOrEmpty()) return 0;
        float perSquad = 0f;
        foreach (PlatoonDef squad in def.squads)
        {
            if (squad == null) continue;
            perSquad += 1f + (squad.fixedPawnkind?.Sum(o => (int)o.selectionWeight) ?? 0) + squad.memberCountRange.Average;
        }
        perSquad /= def.squads.Count;
        return Mathf.RoundToInt(perSquad * Mathf.Max(1f, def.squadCountRange.Average));
    }

    public override string GetPostProcessedThreatLabel(Site site, SitePart sitePart)
    {
        return "KnownSiteThreatEnemyCountAppend".Translate(EstimatedPawnCount(site), "Enemies".Translate());
    }

    public override void Notify_GeneratedByQuestGen(SitePart part, Slate slate, List<Rule> outExtraDescriptionRules, Dictionary<string, string> outExtraDescriptionConstants)
    {
        base.Notify_GeneratedByQuestGen(part, slate, outExtraDescriptionRules, outExtraDescriptionConstants);
        int count = EstimatedPawnCount(part.site);
        float mtbDays = QuestTuning.PointsToRaidSourceRaidsMTBDaysCurve.Evaluate(part.parms.threatPoints);
        outExtraDescriptionRules.Add(new Rule_String("enemiesCount", count.ToString()));
        outExtraDescriptionRules.Add(new Rule_String("mtbDays", ((int)(mtbDays * GenDate.TicksPerDay)).ToStringTicksToPeriod(allowSeconds: true, shortForm: false, canUseDecimals: false)));
        outExtraDescriptionRules.Add(new Rule_String("enemiesLabel", GetEnemiesLabel(part.site, count)));
    }
}
