using System.Collections.Generic;
using Verse;

namespace Fortified;

/// <summary>
/// 所有 ReadinessDef 的存檔與計時（每 250 tick 檢查一次）。外部請透過 <see cref="ReadinessUtility"/> 操作。
/// Saves and ticks every ReadinessDef (checked every 250 ticks). Go through <see cref="ReadinessUtility"/>.
/// </summary>
public class GameComponent_Readiness : GameComponent
{
    private const int CheckInterval = 250;
    private const int HuntdownSyncInterval = 2500;

    private List<ReadinessState> states = new List<ReadinessState>();

    public GameComponent_Readiness(Game game) { }

    public List<ReadinessState> StatesForReading => states;

    /// <summary>取得（必要時建立）某個 def 的狀態。Gets, creating if needed, the state of a def.</summary>
    public ReadinessState StateOf(ReadinessDef def)
    {
        if (def == null) return null;
        for (int i = 0; i < states.Count; i++)
        {
            if (states[i].def == def) return states[i];
        }
        ReadinessState state = new ReadinessState
        {
            def = def,
            level = def.startingLevel,
            peakLevel = def.startingLevel,
        };
        states.Add(state);
        if (Current.ProgramState == ProgramState.Playing)
        {
            state.ScheduleDrop();
            state.ScheduleAction();
        }
        return state;
    }

    public override void FinalizeInit()
    {
        base.FinalizeInit();
        states.RemoveAll(s => s?.def == null);
        foreach (ReadinessDef def in DefDatabase<ReadinessDef>.AllDefsListForReading)
        {
            ReadinessState state = StateOf(def);
            state.level = UnityEngine.Mathf.Clamp(state.level, 0, def.MaxLevel);
            // 舊存檔或新加入的 def 沒有排程。Old saves and newly added defs have no schedule yet.
            if (state.nextDropTick == -1) state.ScheduleDrop();
            if (state.nextActionTick == -1) state.ScheduleAction();
        }
    }

    public override void GameComponentTick()
    {
        int now = Find.TickManager.TicksGame;
        if (states.Count == 0 || now % CheckInterval != 0) return;

        for (int i = 0; i < states.Count; i++)
        {
            ReadinessState state = states[i];
            ReadinessWorker worker = state.def.Worker;
            if (worker.Faction == null) continue;

            // 升到追緝等級時若沒有家園地圖（遠征、重力船上）就啟動不了，這裡定期補試。
            // Reaching the huntdown level with no home map can't start it; retry periodically.
            // 宣戰檢查要掃過所有聚落，跟追緝同步放在較慢的節奏。The war check scans every settlement; keep it on the slow beat.
            if (now % HuntdownSyncInterval == 0)
            {
                worker.SyncHuntdown(state);
                worker.CheckWar(state);
            }

            if (state.nextDropTick > 0 && now >= state.nextDropTick)
            {
                ReadinessUtility.Drop(state.def, 1, "decay");
                continue;
            }
            if (state.nextActionTick > 0 && now >= state.nextActionTick)
            {
                state.ScheduleAction();
                worker.RunActions(state);
            }
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref states, "states", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            states ??= new List<ReadinessState>();
            states.RemoveAll(s => s?.def == null);
        }
    }
}
