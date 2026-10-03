using System;
using System.Collections.Generic;
using Verse;

namespace Fortified;

/// <summary>
/// 追緝的全域狀態與計時。外部請透過 <see cref="HuntdownUtility"/> 操作，不直接改這裡的資料。
/// Global huntdown state and ticking. Go through <see cref="HuntdownUtility"/> rather than editing it directly.
/// </summary>
public class GameComponent_Huntdown : GameComponent
{
    private const int CheckInterval = 250;

    private List<HuntdownInstance> instances = new List<HuntdownInstance>();

    public GameComponent_Huntdown(Game game) { }

    public List<HuntdownInstance> InstancesForReading => instances;

    public HuntdownInstance InstanceOf(HuntdownDef def)
    {
        for (int i = 0; i < instances.Count; i++)
        {
            if (instances[i].def == def) return instances[i];
        }
        return null;
    }

    internal HuntdownInstance Add(HuntdownDef def, string source)
    {
        HuntdownInstance instance = new HuntdownInstance
        {
            def = def,
            source = source,
            startedTick = Find.TickManager.TicksGame,
        };
        instances.Add(instance);
        return instance;
    }

    internal bool Remove(HuntdownInstance instance) => instances.Remove(instance);

    public override void GameComponentTick()
    {
        if (instances.Count == 0 || Find.TickManager.TicksGame % CheckInterval != 0) return;

        int now = Find.TickManager.TicksGame;
        for (int i = instances.Count - 1; i >= 0; i--)
        {
            HuntdownInstance instance = instances[i];
            HuntdownDef def = instance.def;
            HuntdownWorker worker = def.Worker;

            if (instance.hasBoundPawn && def.stopWhenBoundPawnLost && worker.BoundPawnLost(instance.boundPawn))
            {
                HuntdownUtility.Stop(def);
                continue;
            }

            if (instance.suspendedUntilTick >= 0)
            {
                if (now < instance.suspendedUntilTick) continue;
                instance.suspendedUntilTick = -1;
                foreach (HuntdownMapTimer timer in instance.timers)
                {
                    HuntdownUtility.Reschedule(def, timer, initial: false);
                }
                worker.OnResumed(instance);
            }

            if (def.retargetPlayerHome && instance.timers.Count == 0 && Find.AnyPlayerHomeMap is Map home)
            {
                HuntdownUtility.TrackMap(def, home);
            }

            for (int j = instance.timers.Count - 1; j >= 0; j--)
            {
                HuntdownMapTimer timer = instance.timers[j];
                try
                {
                    if (!IsLive(timer.map) || !worker.StillHunted(timer.map))
                    {
                        instance.timers.RemoveAt(j);
                        continue;
                    }
                    TickTimer(instance, timer, now);
                }
                catch (Exception ex)
                {
                    Log.ErrorOnce($"[FFF] Huntdown {instance.def.defName} failed on {timer.map}: {ex}", instance.def.shortHash ^ 0x5A17);
                    instance.timers.RemoveAt(j);
                }
            }
        }
    }

    private static void TickTimer(HuntdownInstance instance, HuntdownMapTimer timer, int now)
    {
        HuntdownDef def = instance.def;
        HuntdownWorker worker = def.Worker;
        if (!timer.warned && now >= timer.warningTick)
        {
            timer.warned = true;
            if (def.sendWarningLetter) worker.SendWarningLetter(timer.map);
        }
        // 一次檢查只發一波，讓連續波次之間至少間隔一個檢查週期。
        // At most one wave per check, so back-to-back waves are spaced by at least one interval.
        if (!timer.AllWavesFired(def) && now >= timer.NextWaveTick(def))
        {
            if (!worker.CanFireWave(timer.map))
            {
                // 例如休戰：整張地圖重新排程，之後再預警一次。E.g. a truce: reschedule the map and warn again later.
                HuntdownUtility.Reschedule(def, timer, initial: false);
                return;
            }
            HuntdownWave wave = def.waves[timer.wavesFired];
            worker.TryFireWave(timer.map, wave);
            timer.wavesFired++;
            worker.OnWaveFired(timer.map, wave);
        }
        if (timer.AllWavesFired(def))
        {
            if (def.repeatWhileStaying)
            {
                HuntdownUtility.Reschedule(def, timer, initial: false);
            }
            else
            {
                instance.timers.Remove(timer);
            }
        }
    }

    internal static bool IsLive(Map map)
    {
        return map?.Parent != null && !map.Parent.Destroyed && Find.Maps.Contains(map);
    }

    internal void Notify_MapRemoved(Map map)
    {
        for (int i = 0; i < instances.Count; i++)
        {
            instances[i].timers.RemoveAll(t => t.map == map);
        }
    }

    public override void ExposeData()
    {
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            // 已移除的地圖不能存成參照。Removed maps cannot be saved as references.
            foreach (HuntdownInstance instance in instances)
            {
                instance.timers.RemoveAll(t => !IsLive(t.map));
            }
        }
        Scribe_Collections.Look(ref instances, "instances", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            instances ??= new List<HuntdownInstance>();
            // 定義被移除（模組卸載）時丟棄。Drop instances whose def no longer exists.
            instances.RemoveAll(x => x?.def == null);
        }
    }
}
