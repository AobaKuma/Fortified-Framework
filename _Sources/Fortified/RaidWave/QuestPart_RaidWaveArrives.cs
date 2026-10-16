using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;

namespace Fortified;

/// <summary>
/// Quest part that controls when raid wave pawns are dropped onto the map.
/// After minDelay ticks, checks ShouldSummonNow every 2500 ticks.
/// Forces arrival at maxDelay regardless of map state.
/// </summary>
public class QuestPart_RaidWaveArrives : QuestPartActivable
{
    private const int CheckInterval = 2500;

    public int minDelay;
    public int maxDelay;
    public MapParent mapParent;
    public RaidWaveDef raidWaveDef;

    public override void QuestPartTick()
    {
        // The target map is gone (gravship departure, abandoned settlement, ...). A destroyed
        // world object resolves to null on load, so this also covers saves made before the
        // "mapParent.Destroyed" end signal was wired up. Nothing can land anymore — end the quest.
        if (mapParent == null || mapParent.Destroyed)
        {
            quest.End(QuestEndOutcome.Unknown, sendLetter: false);
            return;
        }

        int now = Find.TickManager.TicksGame;

        if (now < enableTick + minDelay)
            return;

        if (now >= enableTick + maxDelay)
        {
            Complete();
            return;
        }

        // raidWaveDef is null if its def was removed from the load order; fall back to forced arrival.
        if (raidWaveDef != null
            && mapParent.IsHashIntervalTick(CheckInterval)
            && mapParent.Map != null
            && (bool)raidWaveDef.Worker.ShouldSummonNow(mapParent.Map))
        {
            Complete();
        }
    }

    public override void DoDebugWindowContents(Rect innerRect, ref float curY)
    {
        if (base.State != QuestPartState.Enabled) return;

        int now = Find.TickManager.TicksGame;
        int untilMin = enableTick + minDelay - now;
        int untilMax = enableTick + maxDelay - now;

        string text = "";
        if (untilMin >= 0)
            text += $"\nTicks until min delay: {untilMin} ({untilMin.ToStringTicksToPeriodVerbose()})";
        text += $"\nTicks until forced arrival: {untilMax} ({untilMax.ToStringTicksToPeriodVerbose()})";

        if (mapParent?.Map != null && raidWaveDef != null)
            text +=$"\nShouldSummonNow: {(bool)raidWaveDef.Worker.ShouldSummonNow(mapParent.Map)}";

        Vector2 size = Text.CalcSize(text);
        Rect labelRect = new Rect(innerRect.x, curY, innerRect.width, size.y);
        Widgets.Label(labelRect, text);
        curY += labelRect.height + 4f;

        Rect btnRect = new Rect(innerRect.x, curY, 500f, 25f);
        if (Widgets.ButtonText(btnRect, $"Force complete: {this}"))
            Complete();
        curY += btnRect.height;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref mapParent, "mapParent");
        Scribe_Values.Look(ref minDelay, "minDelay");
        Scribe_Values.Look(ref maxDelay, "maxDelay");
        Scribe_Defs.Look(ref raidWaveDef, "raidWaveDef");
    }
}
