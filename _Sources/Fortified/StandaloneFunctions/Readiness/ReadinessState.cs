using RimWorld;
using Verse;

namespace Fortified;

/// <summary>
/// 單一 ReadinessDef 的存檔狀態。時間點一律存絕對 tick，所以讀檔或時間加速都不會讓計時漂移。
/// Saved state of one ReadinessDef. Times are absolute tick stamps, so loading or fast-forwarding never drifts them.
/// </summary>
public class ReadinessState : IExposable
{
    public ReadinessDef def;
    public int level;
    public int peakLevel;
    public float pressure;

    /// <summary>下一次自行降級的時間；-1 表示不降。Tick of the next drop; -1 when it won't drop.</summary>
    public int nextDropTick = -1;

    /// <summary>下一次行動的時間；-1 表示不行動。Tick of the next action; -1 when there is none.</summary>
    public int nextActionTick = -1;

    public int lastChangeTick = -1;

    public Faction warTarget;

    public ReadinessLevel Current => def.LevelAt(level);

    public bool AtWar => warTarget != null;

    public void ExposeData()
    {
        Scribe_Defs.Look(ref def, "def");
        Scribe_Values.Look(ref level, "level");
        Scribe_Values.Look(ref peakLevel, "peakLevel");
        Scribe_Values.Look(ref pressure, "pressure");
        Scribe_Values.Look(ref nextDropTick, "nextDropTick", -1);
        Scribe_Values.Look(ref nextActionTick, "nextActionTick", -1);
        Scribe_Values.Look(ref lastChangeTick, "lastChangeTick", -1);
        Scribe_References.Look(ref warTarget, "warTarget");
    }

    /// <summary>依目前等級重排降級時間。Reschedules the drop for the current level.</summary>
    public void ScheduleDrop()
    {
        ReadinessLevel current = Current;
        nextDropTick = current.decays && level > 0
            ? Find.TickManager.TicksGame + UnityEngine.Mathf.Max(1, (int)(current.decayDays.RandomInRange * GenDate.TicksPerDay))
            : -1;
    }

    /// <summary>
    /// 依目前等級重排行動時間。onlyIfSooner 時只會提前、不會延後（巡邏抵達等「催促」事件用）。
    /// Reschedules the next action for the current level. With onlyIfSooner it only pulls the action forward,
    /// never back (for "hurry up" events like a patrol arriving).
    /// </summary>
    public void ScheduleAction(bool onlyIfSooner = false)
    {
        ReadinessLevel current = Current;
        if (!current.Acts)
        {
            nextActionTick = -1;
            return;
        }
        int tick = Find.TickManager.TicksGame + UnityEngine.Mathf.Max(1, (int)(current.actionIntervalDays.RandomInRange * GenDate.TicksPerDay));
        if (onlyIfSooner && nextActionTick > 0 && nextActionTick <= tick) return;
        nextActionTick = tick;
    }
}
