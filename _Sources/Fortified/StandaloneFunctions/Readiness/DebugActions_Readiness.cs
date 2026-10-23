using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Fortified;

public static class DebugActions_Readiness
{
    private const string Category = "Fortified";

    private static List<DebugActionNode> PerDef(Action<ReadinessDef> action)
    {
        return DefDatabase<ReadinessDef>.AllDefsListForReading
            .Select(def => new DebugActionNode($"{def.defName} (Lv {ReadinessUtility.LevelOf(def)})", DebugActionType.Action, () => action(def)))
            .ToList();
    }

    [DebugAction(Category, "Readiness: raise", allowedGameStates = AllowedGameStates.Playing)]
    private static List<DebugActionNode> Raise() => PerDef(def => ReadinessUtility.Raise(def, 1, "Debug"));

    [DebugAction(Category, "Readiness: drop", allowedGameStates = AllowedGameStates.Playing)]
    private static List<DebugActionNode> Drop() => PerDef(def => ReadinessUtility.Drop(def, 1, "Debug"));

    [DebugAction(Category, "Readiness: run actions now", allowedGameStates = AllowedGameStates.Playing)]
    private static List<DebugActionNode> RunActions() => PerDef(ReadinessUtility.RunActionsNow);

    [DebugAction(Category, "Readiness: add 0.25 pressure", allowedGameStates = AllowedGameStates.Playing)]
    private static List<DebugActionNode> AddPressure() => PerDef(def => ReadinessUtility.AddPressure(def, 0.25f, "Debug"));

    [DebugAction(Category, "Readiness: deploy company", allowedGameStates = AllowedGameStates.Playing)]
    private static List<DebugActionNode> Deploy() => PerDef(def =>
    {
        ReadinessState state = ReadinessUtility.StateOf(def);
        Settlement target = CompanyUtility.NextObjective(def, state)
            ?? Find.WorldObjects.SettlementBases.Where(s => s.Faction != null && !s.Faction.IsPlayer && s.Faction != def.Worker.Faction).RandomElementWithFallback();
        if (CompanyUtility.Deploy(def, state, target) == null) Log.Warning($"[FortifiedFramework] Could not deploy a company for {def.defName}.");
    });

    [DebugAction(Category, "Readiness: order every company", allowedGameStates = AllowedGameStates.Playing)]
    private static List<DebugActionNode> OrderAll() => PerDef(def =>
    {
        foreach (FFF_Company company in CompanyUtility.CompaniesOf(def.Worker.Faction)) company.IssueOrder();
    });

    [DebugAction(Category, "Readiness: company raid here", allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static List<DebugActionNode> RaidHere() => PerDef(def =>
        CompanyUtility.RaidMap(Find.CurrentMap, CompanyUtility.RandomCompanyDef(def, def.MaxLevel), def.Worker.Faction));

    [DebugAction(Category, "Readiness: spawn patrol here", allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static void SpawnPatrolHere()
    {
        Map map = Find.CurrentMap;
        foreach (WorldObject obj in Find.WorldObjects.ObjectsAt(map.Tile))
        {
            obj.GetComponent<WorldObjectComp_ReadinessPatrol>()?.SpawnPatrol();
        }
    }

    [DebugAction(Category, "Readiness: log status", allowedGameStates = AllowedGameStates.Playing)]
    private static void LogStatus()
    {
        int now = Find.TickManager.TicksGame;
        foreach (ReadinessState state in ReadinessUtility.Component?.StatesForReading ?? new List<ReadinessState>())
        {
            Log.Message($"[Readiness] {state.def.defName}: level {state.level}/{state.def.MaxLevel} (peak {state.peakLevel}), pressure {state.pressure:0.00}, "
                + $"drop in {(state.nextDropTick > 0 ? (state.nextDropTick - now).ToStringTicksToPeriod() : "never")}, "
                + $"action in {(state.nextActionTick > 0 ? (state.nextActionTick - now).ToStringTicksToPeriod() : "never")}, "
                + $"war {state.warTarget?.Name ?? "none"}, companies {CompanyUtility.CompaniesOf(state.def.Worker.Faction).Count}");
        }
    }
}
