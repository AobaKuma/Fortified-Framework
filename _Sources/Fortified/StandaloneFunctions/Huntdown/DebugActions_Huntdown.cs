using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace Fortified;

public static class DebugActions_Huntdown
{
    private const string Category = "Fortified";

    [DebugAction(Category, "Huntdown: start on current map", allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static List<DebugActionNode> StartOnCurrentMap()
    {
        return DefDatabase<HuntdownDef>.AllDefsListForReading
            .Select(def => new DebugActionNode(def.defName, DebugActionType.Action,
                () => HuntdownUtility.Start(def, Find.CurrentMap, initial: true, source: "Debug")))
            .ToList();
    }

    [DebugAction(Category, "Huntdown: fire next wave", allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static List<DebugActionNode> FireNextWave()
    {
        return HuntdownUtility.HuntersOf(Find.CurrentMap)
            .Select(x => new DebugActionNode(x.def.defName, DebugActionType.Action,
                () => HuntdownUtility.FireNextWaveNow(x.def, Find.CurrentMap)))
            .ToList();
    }

    [DebugAction(Category, "Huntdown: stop", allowedGameStates = AllowedGameStates.Playing)]
    private static List<DebugActionNode> Stop()
    {
        return HuntdownUtility.AllActive
            .Select(x => new DebugActionNode(x.def.defName, DebugActionType.Action, () => HuntdownUtility.Stop(x.def)))
            .ToList();
    }

    [DebugAction(Category, "Huntdown: suspend 1 day / resume", allowedGameStates = AllowedGameStates.Playing)]
    private static List<DebugActionNode> ToggleSuspend()
    {
        return HuntdownUtility.AllActive
            .Select(x => new DebugActionNode(x.def.defName + (x.Suspended ? " (resume)" : " (suspend)"), DebugActionType.Action, () =>
            {
                if (x.Suspended) HuntdownUtility.ResumeNow(x.def);
                else HuntdownUtility.Suspend(x.def, GenDate.TicksPerDay);
            }))
            .ToList();
    }

    [DebugAction(Category, "Huntdown: log status", allowedGameStates = AllowedGameStates.Playing)]
    private static void LogStatus()
    {
        int now = Find.TickManager.TicksGame;
        List<string> lines = new List<string> { $"[FFF] Huntdowns at tick {now}:" };
        foreach (HuntdownInstance instance in HuntdownUtility.AllActive)
        {
            lines.Add($"- {instance.def.defName} (source: {instance.source ?? "?"}, started {instance.startedTick}, " +
                $"bound: {(instance.hasBoundPawn ? instance.boundPawn?.LabelShort ?? "lost" : "-")}, " +
                $"suspended: {(instance.Suspended ? instance.SuspendedTicksLeft.ToString() : "no")})");
            foreach (HuntdownMapTimer timer in instance.timers)
            {
                lines.Add($"    {timer.map}: warning {timer.warningTick - now:+#;-#;0}, " +
                    $"next wave {HuntdownUtility.TicksUntilNextWave(instance.def, timer.map)}, " +
                    $"waves fired {timer.wavesFired}/{instance.def.waves.Count}");
            }
        }
        Log.Message(string.Join("\n", lines));
    }
}
