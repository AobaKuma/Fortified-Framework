using System.Collections.Generic;
using Verse;

namespace Fortified;

/// <summary>
/// 一個進行中的追緝：哪個定義、由誰啟動、目前追緝哪些地圖。
/// An active huntdown: its def, who started it, and the maps it currently hunts.
/// </summary>
public class HuntdownInstance : IExposable
{
    public HuntdownDef def;

    /// <summary>啟動來源（任務 id、開局等），僅供除錯與查詢。Who started it (quest id, scenario…), for debugging.</summary>
    public string source;

    public int startedTick;

    public List<HuntdownMapTimer> timers = new List<HuntdownMapTimer>();

    public HuntdownMapTimer TimerFor(Map map)
    {
        for (int i = 0; i < timers.Count; i++)
        {
            if (timers[i].map == map) return timers[i];
        }
        return null;
    }

    public void ExposeData()
    {
        Scribe_Defs.Look(ref def, "def");
        Scribe_Values.Look(ref source, "source");
        Scribe_Values.Look(ref startedTick, "startedTick");
        Scribe_Collections.Look(ref timers, "timers", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            timers ??= new List<HuntdownMapTimer>();
            timers.RemoveAll(t => t?.map == null);
        }
    }
}

/// <summary>
/// 單一地圖上的追緝排程。所有 tick 為絕對遊戲時間。
/// The huntdown schedule on one map. All ticks are absolute game ticks.
/// </summary>
public class HuntdownMapTimer : IExposable
{
    public Map map;

    public int warningTick;

    /// <summary>首波時間；後續波次為 raidTick + wave.delayTicks。First-wave tick; later waves add wave.delayTicks.</summary>
    public int raidTick;

    public bool warned;

    public int wavesFired;

    public bool AllWavesFired(HuntdownDef def) => wavesFired >= def.waves.Count;

    public int NextWaveTick(HuntdownDef def) => AllWavesFired(def) ? -1 : raidTick + def.waves[wavesFired].delayTicks;

    public int LastWaveTick(HuntdownDef def) => raidTick + def.LastWaveDelay;

    public void ExposeData()
    {
        Scribe_References.Look(ref map, "map");
        Scribe_Values.Look(ref warningTick, "warningTick");
        Scribe_Values.Look(ref raidTick, "raidTick");
        Scribe_Values.Look(ref warned, "warned");
        Scribe_Values.Look(ref wavesFired, "wavesFired");
    }
}
