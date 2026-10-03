using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified;

/// <summary>
/// 追緝的行為；需要自訂條件、信件或襲擊方式時繼承並在 HuntdownDef.workerClass 指定。
/// Huntdown behaviour; subclass and set HuntdownDef.workerClass to customise conditions, letters or raids.
/// </summary>
public class HuntdownWorker
{
    public HuntdownDef def;

    public virtual Faction Faction => Find.FactionManager.FirstFactionOfDef(def.faction);

    /// <summary>這張地圖能否開始被追緝。Whether this map may start being hunted.</summary>
    public virtual bool CanTrackMap(Map map)
    {
        if (map?.Parent == null || map.Parent.Destroyed) return false;
        if (def.IsExcluded(map.generatorDef)) return false;
        return Faction != null;
    }

    /// <summary>
    /// 已追緝的地圖是否仍符合條件；回傳 false 時該地圖的計時會被移除。
    /// Whether a tracked map is still hunted; returning false drops its timer.
    /// </summary>
    public virtual bool StillHunted(Map map)
    {
        if (def.requireGravEngine && GravshipUtility.GetPlayerGravEngine_NewTemp(map) == null) return false;
        if (!def.requiredThings.NullOrEmpty() && FirstThingOf(map, def.requiredThings) == null) return false;
        return true;
    }

    public virtual void SendWarningLetter(Map map)
    {
        Faction faction = Faction;
        Thing lookTarget = FirstThingOf(map, def.letterLookTargets) ?? FirstThingOf(map, def.requiredThings);
        Find.LetterStack.ReceiveLetter(
            def.LetterLabel.Formatted(faction.NameColored),
            def.LetterText.Formatted(faction.NameColored),
            LetterDefOf.ThreatSmall,
            lookTarget != null ? new LookTargets(lookTarget) : new LookTargets(map.Parent));
    }

    public virtual bool TryFireWave(Map map, HuntdownWave wave)
    {
        Faction faction = Faction;
        if (faction == null) return false;
        IncidentParms parms = new IncidentParms
        {
            forced = true,
            target = map,
            points = Mathf.Max(wave.minPoints, StorytellerUtility.DefaultThreatPointsNow(map) * wave.pointsMultiplier),
            faction = faction,
            raidArrivalMode = def.RaidArrivalMode,
            raidStrategy = def.RaidStrategy,
        };
        return IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
    }

    /// <summary>啟動時套用一次（好感度等）。Applied once when the huntdown starts (goodwill etc.).</summary>
    public virtual void OnStarted()
    {
        Faction faction = Faction;
        if (faction != null && def.goodwillChangeOnStart != 0 && faction != Faction.OfPlayer)
        {
            faction.ChangeGoodwill_Debug(Faction.OfPlayer, def.goodwillChangeOnStart);
        }
    }

    public virtual void OnStopped()
    {
    }

    protected static Thing FirstThingOf(Map map, List<ThingDef> defs)
    {
        if (map == null || defs.NullOrEmpty()) return null;
        foreach (ThingDef thingDef in defs)
        {
            List<Thing> things = map.listerThings.ThingsOfDef(thingDef);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].Spawned) return things[i];
            }
        }
        return null;
    }
}
