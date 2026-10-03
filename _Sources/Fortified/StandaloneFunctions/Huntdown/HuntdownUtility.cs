using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Fortified;

/// <summary>
/// 追緝的管理與調用入口。開局、任務、建築或其他模組都只需呼叫這裡。
///
/// Entry point for managing and invoking huntdowns, from scenarios, quests, buildings or other mods.
/// <code>
/// HuntdownUtility.Start(def, map);            // 啟動並立即追緝這張地圖 start and hunt this map
/// HuntdownUtility.Stop(def);                  // 結束（例如任務完成）stop, e.g. on quest completion
/// HuntdownUtility.Delay(def, map, 60000);     // 干擾、延後 jam / push back the schedule
/// HuntdownUtility.TicksUntilNextWave(def, map);
/// </code>
/// </summary>
public static class HuntdownUtility
{
    public static GameComponent_Huntdown Component => Current.Game?.GetComponent<GameComponent_Huntdown>();

    // ── 查詢 Queries ────────────────────────────────────────────────────────

    public static bool IsActive(HuntdownDef def) => Component?.InstanceOf(def) != null;

    public static HuntdownInstance GetInstance(HuntdownDef def) => Component?.InstanceOf(def);

    public static IEnumerable<HuntdownInstance> AllActive =>
        Component?.InstancesForReading ?? Enumerable.Empty<HuntdownInstance>();

    /// <summary>地圖是否被任一（或指定）追緝追蹤中。Whether the map is hunted by any huntdown, or by def.</summary>
    public static bool IsMapHunted(Map map, HuntdownDef def = null)
    {
        return HuntersOf(map).Any(x => def == null || x.def == def);
    }

    /// <summary>正在追緝這張地圖的所有追緝。All huntdowns currently hunting the map.</summary>
    public static IEnumerable<HuntdownInstance> HuntersOf(Map map)
    {
        if (map == null) yield break;
        foreach (HuntdownInstance instance in AllActive)
        {
            if (instance.TimerFor(map) != null) yield return instance;
        }
    }

    /// <summary>距下一波的 tick 數；沒有排程時回傳 -1。Ticks until the next wave, or -1 when none is scheduled.</summary>
    public static int TicksUntilNextWave(HuntdownDef def, Map map)
    {
        HuntdownMapTimer timer = GetInstance(def)?.TimerFor(map);
        if (timer == null || timer.AllWavesFired(def)) return -1;
        return System.Math.Max(0, timer.NextWaveTick(def) - Find.TickManager.TicksGame);
    }

    // ── 啟動與結束 Start / stop ─────────────────────────────────────────────

    /// <summary>
    /// 啟動追緝。已啟動時不重複套用 OnStarted，只補追蹤地圖。
    /// map 為 null 時只啟動、不排程，等重力船降落或之後呼叫 TrackMap。
    /// initial = true 時用較短的 initialWarningDelay / initialRaidDelay（開局地圖）。
    ///
    /// Starts the huntdown; if it is already active only the map is added.
    /// With map null nothing is scheduled until a gravship landing or a later TrackMap.
    /// initial uses the short initial delays meant for the starting map.
    /// </summary>
    public static HuntdownInstance Start(HuntdownDef def, Map map = null, bool initial = false, string source = null, Pawn boundPawn = null)
    {
        GameComponent_Huntdown component = Component;
        if (def == null || component == null) return null;

        HuntdownInstance instance = component.InstanceOf(def);
        if (instance == null)
        {
            instance = component.Add(def, source);
            def.Worker.OnStarted();
        }
        if (boundPawn != null) BindPawn(def, boundPawn);
        if (map != null) TrackMap(def, map, initial);
        return instance;
    }

    /// <summary>結束追緝並清除所有地圖的排程。Stops the huntdown and clears every map's schedule.</summary>
    public static bool Stop(HuntdownDef def)
    {
        GameComponent_Huntdown component = Component;
        HuntdownInstance instance = component?.InstanceOf(def);
        if (instance == null) return false;
        component.Remove(instance);
        def.Worker.OnStopped();
        return true;
    }

    public static void StopAll()
    {
        foreach (HuntdownInstance instance in AllActive.ToList())
        {
            Stop(instance.def);
        }
    }

    // ── 綁定角色 Bound pawn ─────────────────────────────────────────────────

    /// <summary>把追緝綁定到一名角色（取代先前的綁定）。Binds the huntdown to a pawn, replacing any earlier one.</summary>
    public static bool BindPawn(HuntdownDef def, Pawn pawn)
    {
        HuntdownInstance instance = GetInstance(def);
        if (instance == null || pawn == null) return false;
        instance.boundPawn = pawn;
        instance.hasBoundPawn = true;
        return true;
    }

    public static IEnumerable<HuntdownInstance> HuntdownsBoundTo(Pawn pawn)
    {
        if (pawn == null) yield break;
        foreach (HuntdownInstance instance in AllActive)
        {
            if (instance.boundPawn == pawn) yield return instance;
        }
    }

    /// <summary>
    /// 結束所有綁定此角色的追緝（例如軍事法庭審判了他），回傳結束的數量。
    /// Stops every huntdown bound to the pawn (e.g. they stood trial at a court-martial); returns how many.
    /// </summary>
    public static int StopAllBoundTo(Pawn pawn)
    {
        int count = 0;
        foreach (HuntdownInstance instance in HuntdownsBoundTo(pawn).ToList())
        {
            if (Stop(instance.def)) count++;
        }
        return count;
    }

    // ── 暫停 Suspension ────────────────────────────────────────────────────

    public static bool IsSuspended(HuntdownDef def) => GetInstance(def)?.Suspended ?? false;

    public static int SuspendedTicksLeft(HuntdownDef def) => GetInstance(def)?.SuspendedTicksLeft ?? 0;

    /// <summary>
    /// 暫停追緝；已在暫停中時疊加在目前的結束時間之後。結束時所有地圖重新排程。回傳暫停剩餘 ticks。
    /// Suspends the huntdown, stacking onto a running suspension. Every map is rescheduled when it ends.
    /// Returns the ticks left.
    /// </summary>
    public static int Suspend(HuntdownDef def, int ticks)
    {
        HuntdownInstance instance = GetInstance(def);
        if (instance == null) return 0;
        bool wasSuspended = instance.Suspended;
        int now = Find.TickManager.TicksGame;
        instance.suspendedUntilTick = System.Math.Max(now, instance.suspendedUntilTick) + System.Math.Max(0, ticks);
        if (!wasSuspended) def.Worker.OnSuspended(instance);
        return instance.SuspendedTicksLeft;
    }

    /// <summary>在下一次檢查時結束暫停。Ends the suspension at the next check.</summary>
    public static bool ResumeNow(HuntdownDef def)
    {
        HuntdownInstance instance = GetInstance(def);
        if (instance == null || instance.suspendedUntilTick < 0) return false;
        instance.suspendedUntilTick = Find.TickManager.TicksGame;
        return true;
    }

    // ── 地圖 Maps ──────────────────────────────────────────────────────────

    /// <summary>
    /// 讓已啟動的追緝追蹤這張地圖；地圖已在追蹤中時保留原排程。
    /// Makes an active huntdown hunt this map; an already tracked map keeps its schedule.
    /// </summary>
    public static bool TrackMap(HuntdownDef def, Map map, bool initial = false)
    {
        HuntdownInstance instance = GetInstance(def);
        if (instance == null || map == null) return false;
        if (instance.TimerFor(map) != null) return true;
        if (!def.Worker.CanTrackMap(map)) return false;

        HuntdownMapTimer timer = new HuntdownMapTimer { map = map };
        Reschedule(def, timer, initial);
        instance.timers.Add(timer);
        return true;
    }

    public static bool UntrackMap(HuntdownDef def, Map map)
    {
        HuntdownInstance instance = GetInstance(def);
        return instance != null && instance.timers.RemoveAll(t => t.map == map) > 0;
    }

    /// <summary>
    /// 把這張地圖尚未發生的預警與波次延後 ticks（負值 = 提前）。
    /// Pushes back the map's pending warning and waves by ticks (negative = earlier).
    /// </summary>
    public static bool Delay(HuntdownDef def, Map map, int ticks)
    {
        HuntdownMapTimer timer = GetInstance(def)?.TimerFor(map);
        if (timer == null || timer.AllWavesFired(def)) return false;
        if (!timer.warned) timer.warningTick += ticks;
        timer.raidTick += ticks;
        return true;
    }

    /// <summary>立即發動下一波（除錯或劇情用）。Fires the next wave right now, for debugging or story beats.</summary>
    public static bool FireNextWaveNow(HuntdownDef def, Map map)
    {
        HuntdownMapTimer timer = GetInstance(def)?.TimerFor(map);
        if (timer == null || timer.AllWavesFired(def)) return false;
        timer.raidTick = Find.TickManager.TicksGame - def.waves[timer.wavesFired].delayTicks;
        timer.warningTick = System.Math.Min(timer.warningTick, Find.TickManager.TicksGame);
        return true;
    }

    internal static void Reschedule(HuntdownDef def, HuntdownMapTimer timer, bool initial)
    {
        int now = Find.TickManager.TicksGame;
        timer.warningTick = now + (initial ? def.initialWarningDelay : def.warningDelayRange.RandomInRange);
        timer.raidTick = now + (initial ? def.initialRaidDelay : def.raidDelayRange.RandomInRange);
        if (timer.raidTick < timer.warningTick) timer.raidTick = timer.warningTick;
        timer.warned = false;
        timer.wavesFired = 0;
    }

    // ── 原版事件掛鉤（由 Harmony 呼叫）Vanilla hooks, called from Harmony ────

    internal static void Notify_GravshipLanded(Map map)
    {
        foreach (HuntdownInstance instance in AllActive.ToList())
        {
            if (instance.def.followGravship) TrackMap(instance.def, map);
        }
    }

    internal static void Notify_MapRemoved(Map map)
    {
        Component?.Notify_MapRemoved(map);
    }
}
