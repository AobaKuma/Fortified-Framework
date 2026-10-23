using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Fortified;

/// <summary>
/// 世界地圖上的連隊。會圍在目標聚落旁、依 CompanyDef 的機率接管／分兵／夷平，閒置時遊走或撤回；
/// 玩家攻入時生成整個連隊作為守軍。
///
/// A company on the world map. It camps beside its target settlement and, by its CompanyDef's odds, takes it over,
/// splits off or razes it; idle, it roams or withdraws. When the player attacks, the whole company is the garrison.
/// </summary>
public class FFF_Company : Site
{
    protected TaggedString name;
    protected Settlement targetSettlement;
    protected CompanyDef companyDef;

    public bool HasTarget => targetSettlement != null;
    public Settlement Target => targetSettlement;
    public TaggedString CompanyName => name;

    public ReadinessDef Readiness => ReadinessUtility.DefFor(Faction);

    public CompanyDef CompanyDef
    {
        get
        {
            if (companyDef == null && Faction != null)
            {
                companyDef = CompanyUtility.RandomCompanyDef(Faction.def, ReadinessUtility.LevelOf(Faction));
            }
            return companyDef;
        }
    }

    public override string Label => name.NullOrEmpty() ? base.Label : name.RawText;

    public override Texture2D ExpandingIcon
    {
        get
        {
            Texture2D icon = companyDef != null ? ContentFinder<Texture2D>.Get(companyDef.IconPath, reportFailure: false) : null;
            return icon ?? base.ExpandingIcon;
        }
    }

    public override string GetDescription()
    {
        string text = base.GetDescription();
        if (companyDef != null && Faction != null)
        {
            text += "\n\n" + (targetSettlement != null
                ? "FFF_Company_DescTarget".Translate(Faction.Name, companyDef.label, targetSettlement.Name).RawText
                : "FFF_Company_DescNoTarget".Translate(Faction.Name, companyDef.label).RawText);
        }
        return text;
    }

    public void SetCompany(CompanyDef def)
    {
        companyDef = def;
    }

    public override void PostMake()
    {
        base.PostMake();
        SitePartDef part = def.GetModExtension<ModExtension_CompanyWorldObject>()?.sitePart;
        if (part == null)
        {
            Log.ErrorOnce($"[FortifiedFramework] {def.defName} is an FFF_Company without ModExtension_CompanyWorldObject.sitePart.", def.shortHash ^ 0x5C0A);
            return;
        }
        Map home = Find.AnyPlayerHomeMap;
        AddPart(new SitePart(this, part, new SitePartParams
        {
            threatPoints = home != null ? StorytellerUtility.DefaultThreatPointsNow(home) : 500f,
        }));
    }

    public override void SpawnSetup()
    {
        base.SpawnSetup();
        // 連隊一律屬於編制的派系：缺派系或被設成玩家時改回 CompanyDef 的預設派系。
        // A company always belongs to its roster's faction: missing or player is reset to the CompanyDef's default.
        if ((Faction == null || Faction.IsPlayer) && companyDef?.defaultFaction != null)
        {
            Faction owner = Find.FactionManager.FirstFactionOfDef(companyDef.defaultFaction);
            if (owner != null) SetFaction(owner);
        }
        if (CompanyDef == null)
        {
            Log.Error($"[FortifiedFramework] No CompanyDef for faction {Faction?.def?.defName ?? "null"}; removing {def.defName}.");
            Destroy();
            return;
        }
        if (name.NullOrEmpty() && companyDef.NamePack != null) name = NameGenerator.GenerateName(companyDef.NamePack);
    }

    // ── 移動 Movement ──────────────────────────────────────────────────────

    /// <summary>
    /// 鎖定目標並移到作戰半徑內一格可通行、無人、非地標的地塊。找不到落腳點時回傳 false（目標仍會記下）。
    /// Targets the settlement and moves to a passable, empty, non-landmark tile within the combat radius.
    /// Returns false when no tile fits (the target is still recorded).
    /// </summary>
    public bool SetTarget(Settlement settlement)
    {
        if (settlement == null) return false;
        targetSettlement = settlement;
        int radius = CompanyDef?.combatRadius ?? 10;
        List<PlanetTile> tiles = CompanyUtility.NearbyTiles(settlement.Tile, radius)
            .FindAll(t => !Find.World.Impassable(t) && !Find.WorldObjects.AnyWorldObjectAt(t))
            .WithoutLandmarks();
        if (tiles.Count == 0) return false;
        Tile = tiles.RandomElement();
        return true;
    }

    private void MoveBeside(PlanetTile tile)
    {
        List<PlanetTile> neighbours = new List<PlanetTile>();
        Find.WorldGrid.GetTileNeighbors(tile, neighbours);
        neighbours.RemoveAll(t => Find.World.Impassable(t) || Find.WorldObjects.AnyWorldObjectAt(t));
        PlanetTile next = CompanyUtility.ClosestTile(neighbours.WithoutLandmarks(), Tile);
        if (next.Valid) Tile = next;
    }

    // ── 指令 Orders ────────────────────────────────────────────────────────

    /// <summary>
    /// 下一道指令。有目標：按兵不動 → 接管 → 分兵 → 夷平（依序擲骰）；沒目標：撤回，或領取戰爭目標，或遊走。
    /// Issues one order. With a target: hold → take over → split → raze (rolled in order). Without: withdraw,
    /// take the next war objective, or roam.
    /// </summary>
    public virtual void IssueOrder()
    {
        CompanyDef company = CompanyDef;
        if (company == null) return;
        if (targetSettlement != null && (targetSettlement.Destroyed || !targetSettlement.Spawned || targetSettlement.Faction == Faction))
        {
            targetSettlement = null;
        }
        ReadinessDef readiness = Readiness;
        if (targetSettlement != null)
        {
            if (Rand.Chance(company.holdChance)) return;
            if (Rand.Chance(company.takeoverChance))
            {
                PlanetTile tile = targetSettlement.Tile;
                Settlement victim = targetSettlement;
                targetSettlement = null;
                if (readiness != null) readiness.Worker.Takeover(victim);
                MoveBeside(tile);
                return;
            }
            if (Rand.Chance(company.splitChance) && readiness != null)
            {
                Settlement victim = targetSettlement;
                targetSettlement = null;
                CompanyUtility.Deploy(readiness, ReadinessUtility.StateOf(readiness), victim);
                return;
            }
            if (Rand.Chance(company.razeChance))
            {
                Raze(targetSettlement);
                targetSettlement = null;
            }
            return;
        }

        if (Rand.Chance(company.idleDisbandChance))
        {
            Destroy();
            return;
        }
        ReadinessState state = readiness != null ? ReadinessUtility.StateOf(readiness) : null;
        if (state != null && state.AtWar)
        {
            Settlement objective = CompanyUtility.NextObjective(readiness, state);
            if (objective != null) SetTarget(objective);
            else readiness.Worker.CheckWar(state);
            return;
        }
        PlanetTile roam = CompanyUtility.NearbyTiles(Tile, Mathf.Max(1, company.combatRadius / 2))
            .FindAll(t => !Find.World.Impassable(t) && !Find.WorldObjects.AnyWorldObjectAt(t))
            .WithoutLandmarks()
            .RandomElementWithFallback(Tile);
        if (roam != Tile) Tile = roam;
    }

    protected virtual void Raze(Settlement settlement)
    {
        PlanetTile tile = settlement.Tile;
        settlement.Destroy();
        SettlementDefeatUtility.CheckDefeated(settlement);
        DestroyedSettlement ruins = (DestroyedSettlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.DestroyedSettlement);
        ruins.Tile = tile;
        Find.WorldObjects.Add(ruins);
        MoveBeside(tile);
    }

    // ── 地圖 Map ──────────────────────────────────────────────────────────

    /// <summary>
    /// 保留原版 Site 的所有守衛，只加上「沒有指揮官就連世界物件一起撤走」。
    /// Keeps every vanilla Site guard; only adds "no commander left → remove the world object too".
    /// </summary>
    public override bool ShouldRemoveMapNow(out bool alsoRemoveWorldObject)
    {
        bool result = base.ShouldRemoveMapNow(out alsoRemoveWorldObject);
        if (!result) return false;
        if (Map != null && !Map.IsPlayerHome && Faction != null && CompanyDef != null && !CompanyDef.Worker.HasCommander(Map, Faction))
        {
            alsoRemoveWorldObject = true;
        }
        return result;
    }

    public override void PostMapGenerate()
    {
        base.PostMapGenerate();
        Readiness?.Worker.OnCompanyEngaged(this);
        GenerateGarrison();
    }

    public virtual void GenerateGarrison()
    {
        if (!HasMap || CompanyDef == null || Faction == null) return;
        IntVec3 center = Map.Center;
        Lord lord = companyDef.Worker.MakeGarrisonLord(Faction, Map, center);
        foreach (PawnKindDef kind in companyDef.GeneratePawnKinds())
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, Faction, PawnGenerationContext.NonPlayer, tile: Tile, inhabitant: true));
            GenPlace.TryPlaceThing(pawn, center + (20f * Rand.InsideUnitCircleVec3).ToIntVec3(), Map, ThingPlaceMode.Near);
            lord.AddPawn(pawn);
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        // 鍵名沿用舊版，讓舊存檔的連隊照常讀入。Keys keep their old names so existing companies load.
        Scribe_Values.Look(ref name, "CompanyName");
        Scribe_Defs.Look(ref companyDef, "CompanyDef");
        Scribe_References.Look(ref targetSettlement, "TargetSettlement");
    }
}
