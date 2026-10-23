using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Fortified;

/// <summary>
/// 連隊的部署、查詢、襲擊與世界格工具。
/// Deploying, querying and raiding with companies, plus the world-tile helpers they use.
/// </summary>
public static class CompanyUtility
{
    // ── 查詢 Queries ────────────────────────────────────────────────────────

    public static List<FFF_Company> CompaniesOf(Faction faction)
    {
        List<FFF_Company> result = new List<FFF_Company>();
        if (faction == null) return result;
        List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is FFF_Company company && company.Faction == faction && company.Tile.Valid && !company.Destroyed)
            {
                result.Add(company);
            }
        }
        return result;
    }

    /// <summary>此派系在此等級可部署的隨機連隊編制。A random company this faction may deploy at this level.</summary>
    public static CompanyDef RandomCompanyDef(FactionDef faction, int level)
    {
        return DefDatabase<CompanyDef>.AllDefsListForReading
            .Where(c => c.defaultFaction == faction && c.minLevel <= level && !c.squads.NullOrEmpty())
            .RandomElementWithFallback();
    }

    public static CompanyDef RandomCompanyDef(ReadinessDef def, int level) => RandomCompanyDef(def?.faction, level);

    // ── 部署 Deployment ─────────────────────────────────────────────────────

    /// <summary>
    /// 讓連隊數量貼近此等級的上限：和平時解散多出來的，戰時補到上限（找不到落腳點就停手）。
    /// Keeps the company count at the level's cap: trims the excess in peacetime, tops up during a war
    /// (stopping when no tile can be found).
    /// </summary>
    public static void MaintainCompanies(ReadinessDef def, ReadinessState state)
    {
        if (def?.companyWorldObject == null) return;
        Faction faction = def.Worker.Faction;
        List<FFF_Company> companies = CompaniesOf(faction);
        int cap = state.Current.maxCompanies;
        if (!state.AtWar)
        {
            // 先解散沒有目標的，正在圍攻的留到最後。Idle companies go first; besieging ones last.
            foreach (FFF_Company company in companies.OrderBy(c => c.HasTarget ? 1 : 0).Take(Mathf.Max(0, companies.Count - cap)).ToList())
            {
                company.Destroy();
            }
            return;
        }
        for (int i = companies.Count; i < cap; i++)
        {
            if (Deploy(def, state, NextObjective(def, state)) == null) break;
        }
    }

    /// <summary>
    /// 下一個戰爭目標：敵方離我方聚落最近、且還沒有連隊圍攻的聚落；全被圍了就隨便挑一個。
    /// The next war objective: the enemy settlement closest to ours that no company is already on; any one when all are.
    /// </summary>
    public static Settlement NextObjective(ReadinessDef def, ReadinessState state)
    {
        Faction target = state?.warTarget;
        Faction us = def?.Worker.Faction;
        if (target == null || us == null) return null;
        List<Settlement> enemy = Find.WorldObjects.SettlementBases.Where(s => s.Faction == target && s.Tile.Valid).ToList();
        if (enemy.Count == 0) return null;
        HashSet<Settlement> taken = new HashSet<Settlement>(CompaniesOf(us).Where(c => c.HasTarget).Select(c => c.Target));
        List<Settlement> open = enemy.Where(s => !taken.Contains(s)).ToList();
        if (open.Count == 0) return enemy.RandomElement();
        List<Settlement> ours = Find.WorldObjects.SettlementBases.Where(s => s.Faction == us && s.Tile.Valid).ToList();
        Settlement closest = ClosestSettlementPair(open, ours).Item1;
        return closest ?? open.RandomElement();
    }

    /// <summary>
    /// 部署一個連隊到目標旁。target 為 null 或附近沒有落腳點時不部署並回傳 null。
    /// Deploys a company beside the target. Returns null (deploying nothing) when the target is null or has no room nearby.
    /// </summary>
    public static FFF_Company Deploy(ReadinessDef def, ReadinessState state, Settlement target)
    {
        if (def?.companyWorldObject == null || target == null || target.Destroyed) return null;
        Faction faction = def.Worker.Faction;
        if (faction == null) return null;
        CompanyDef companyDef = RandomCompanyDef(def, state?.level ?? 0);
        if (companyDef == null) return null;

        if (WorldObjectMaker.MakeWorldObject(def.companyWorldObject) is not FFF_Company company) return null;
        company.SetFaction(faction);
        company.SetCompany(companyDef);
        if (!company.SetTarget(target)) return null;
        Find.WorldObjects.Add(company);
        return company;
    }

    // ── 襲擊 Raids ────────────────────────────────────────────────────────

    /// <summary>
    /// 連隊直接從地圖邊緣進攻：每一排各自一個 Lord，領隊帶隊。
    /// A company attacks from the map edge: one lord per platoon, led by its leader.
    /// </summary>
    public static bool RaidMap(Map map, CompanyDef company, Faction faction)
    {
        if (map == null || company == null || faction == null || company.squads.NullOrEmpty()) return false;
        CompanyWorker worker = company.Worker;
        List<Pawn> leaders = new List<Pawn>();
        int squadCount = Mathf.Max(1, company.squadCountRange.RandomInRange);
        for (int i = 0; i < squadCount; i++)
        {
            PlatoonDef platoon = company.squads.RandomElement();
            if (platoon == null) continue;
            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, CellFinder.EdgeRoadChance_Hostile)) continue;
            List<Pawn> members = new List<Pawn>();
            foreach (PawnKindDef kind in platoon.GeneratePawnKinds())
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer, tile: map.Tile));
                IntVec3 cell = members.Count == 0 ? entry : CellFinder.RandomClosewalkCellNear(entry, map, 8);
                GenSpawn.Spawn(pawn, cell, map, WipeMode.VanishOrMoveAside);
                members.Add(pawn);
            }
            if (members.Count == 0) continue;
            worker.MakeRaidLord(faction, members[0], members, map);
            leaders.Add(members[0]);
        }
        if (leaders.Count == 0) return false;
        string name = company.NamePack != null ? NameGenerator.GenerateName(company.NamePack) : company.LabelCap.RawText;
        worker.SendRaidLetter(faction, name, leaders);
        return true;
    }

    // ── 世界格 World tiles ─────────────────────────────────────────────────

    /// <summary>陸地上、走 maxDistance 格內的所有地塊（含起點）。Land tiles within maxDistance steps, the root included.</summary>
    public static List<PlanetTile> NearbyTiles(PlanetTile root, int maxDistance)
    {
        List<PlanetTile> result = new List<PlanetTile>();
        if (!root.Valid) return result;
        root.Layer.Filler.FloodFill(root, t => t.Tile.elevation > 0, (t, distance) =>
        {
            if (distance > maxDistance) return true;
            result.Add(t);
            return false;
        });
        return result;
    }

    public static PlanetTile ClosestTile(List<PlanetTile> tiles, PlanetTile to)
    {
        PlanetTile best = PlanetTile.Invalid;
        float bestDistance = float.MaxValue;
        if (tiles == null) return best;
        foreach (PlanetTile tile in tiles)
        {
            float distance = Find.WorldGrid.ApproxDistanceInTiles(tile, to);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = tile;
            }
        }
        return best;
    }

    /// <summary>兩組聚落中距離最近的一對。The closest pair between two settlement lists.</summary>
    public static (Settlement, Settlement, float) ClosestSettlementPair(List<Settlement> a, List<Settlement> b)
    {
        Settlement bestA = null, bestB = null;
        float best = float.MaxValue;
        foreach (Settlement x in a)
        {
            foreach (Settlement y in b)
            {
                float distance = Find.WorldGrid.ApproxDistanceInTiles(x.Tile, y.Tile);
                if (distance < best)
                {
                    best = distance;
                    bestA = x;
                    bestB = y;
                }
            }
        }
        return (bestA, bestB, best);
    }
}
