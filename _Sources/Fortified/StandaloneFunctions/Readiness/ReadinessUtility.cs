using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified;

/// <summary>
/// 戒備系統的操作與查詢入口。建築、任務、站點或其他模組都只需呼叫這裡。
///
/// Entry point for driving and querying readiness, from buildings, quests, sites or other mods.
/// <code>
/// ReadinessUtility.Raise(def);                 // 升一級 rise one level
/// ReadinessUtility.AddPressure(def, 0.25f);    // 累積壓力，滿了才升級 build pressure; rises when full
/// ReadinessUtility.Drop(def);                  // 降一級 drop one level
/// ReadinessUtility.LevelOf(def);               // 目前等級 current level
/// ReadinessUtility.DefFor(faction);            // 派系對應的戒備 the readiness that drives a faction
/// </code>
/// </summary>
public static class ReadinessUtility
{
    public static GameComponent_Readiness Component => Current.Game?.GetComponent<GameComponent_Readiness>();

    // ── 查詢 Queries ────────────────────────────────────────────────────────

    public static ReadinessState StateOf(ReadinessDef def) => Component?.StateOf(def);

    public static int LevelOf(ReadinessDef def) => StateOf(def)?.level ?? 0;

    /// <summary>目前等級的節奏倍率（越小越頻繁）。Tempo of the current level (smaller = more often).</summary>
    public static float TempoOf(ReadinessDef def)
    {
        ReadinessState state = StateOf(def);
        return state == null ? 1f : Mathf.Max(state.Current.tempo, 0.05f);
    }

    public static Faction WarTargetOf(ReadinessDef def) => StateOf(def)?.warTarget;

    /// <summary>驅動這個派系的戒備（第一個 faction 相符的 def）。The readiness driving this faction (first def whose faction matches).</summary>
    public static ReadinessDef DefFor(Faction faction) => faction == null ? null : DefFor(faction.def);

    public static ReadinessDef DefFor(FactionDef factionDef)
    {
        if (factionDef == null) return null;
        List<ReadinessDef> defs = DefDatabase<ReadinessDef>.AllDefsListForReading;
        for (int i = 0; i < defs.Count; i++)
        {
            if (defs[i].faction == factionDef) return defs[i];
        }
        return null;
    }

    public static int LevelOf(Faction faction)
    {
        ReadinessDef def = DefFor(faction);
        return def == null ? 0 : LevelOf(def);
    }

    // ── 改變等級 Changing the level ─────────────────────────────────────────

    /// <summary>升級。已在最高級時只會重置降級計時（持續的挑釁讓警戒維持）。Rise; at the top only the drop timer resets.</summary>
    public static void Raise(ReadinessDef def, int amount = 1, string reason = null)
    {
        ReadinessState state = StateOf(def);
        if (state == null || amount <= 0) return;
        SetLevel(def, state.level + amount, reason);
    }

    public static void Drop(ReadinessDef def, int amount = 1, string reason = null)
    {
        ReadinessState state = StateOf(def);
        if (state == null || amount <= 0) return;
        SetLevel(def, state.level - amount, reason);
    }

    /// <summary>設定等級（夾在 0~MaxLevel）。Sets the level, clamped to 0..MaxLevel.</summary>
    public static void SetLevel(ReadinessDef def, int level, string reason = null)
    {
        ReadinessState state = StateOf(def);
        if (state == null || def.Worker.Faction == null) return;
        int old = state.level;
        int target = Mathf.Clamp(level, 0, def.MaxLevel);
        state.level = target;
        if (target > state.peakLevel) state.peakLevel = target;
        state.ScheduleDrop();
        if (target == old)
        {
            // 同級（例如最高級再被挑釁）只延長，不發信。Same level (e.g. provoked at the top): extend only, no letter.
            return;
        }
        if (target < old && def.dropClearsPressure) state.pressure = 0f;
        state.lastChangeTick = Find.TickManager.TicksGame;
        state.ScheduleAction();
        def.Worker.OnLevelChanged(state, old, reason);
    }

    /// <summary>
    /// 累積壓力；每滿 pressurePerLevel 升一級。用於「多次小事件才升級」，例如巡邏隊陸續抵達、反覆駭入設施。
    /// Builds pressure; every pressurePerLevel rises one level. For "many small events add up", e.g. patrols arriving
    /// one after another or repeated facility hacks.
    /// </summary>
    public static void AddPressure(ReadinessDef def, float amount, string reason = null)
    {
        ReadinessState state = StateOf(def);
        if (state == null || amount <= 0f || def.Worker.Faction == null) return;
        state.pressure += amount;
        int rises = 0;
        while (state.pressure >= def.pressurePerLevel)
        {
            state.pressure -= def.pressurePerLevel;
            rises++;
        }
        if (rises > 0) Raise(def, rises, reason);
        // 最高級時壓力沒有意義，避免無限累積。Pressure means nothing at the top; don't let it pile up.
        if (state.level >= def.MaxLevel) state.pressure = 0f;
    }

    /// <summary>把下一次行動提前（只會提前不會延後）。Pulls the next action forward (never pushes it back).</summary>
    public static void Hasten(ReadinessDef def) => StateOf(def)?.ScheduleAction(onlyIfSooner: true);

    /// <summary>立即執行一次目前等級的行動表。Runs the current level's action table now.</summary>
    public static void RunActionsNow(ReadinessDef def)
    {
        ReadinessState state = StateOf(def);
        if (state == null || def.Worker.Faction == null) return;
        def.Worker.RunActions(state);
    }

    // ── 文字 Text ──────────────────────────────────────────────────────────

    /// <summary>
    /// 欄位可以是 Keyed 鍵（能翻譯就翻譯）或直接文字；空白時用 fallbackKey。
    /// A field may be a Keyed key (translated when it resolves) or literal text; empty falls back to fallbackKey.
    /// </summary>
    public static string KeyOrText(string text, string fallbackKey)
    {
        if (text.NullOrEmpty()) return fallbackKey.NullOrEmpty() ? string.Empty : fallbackKey.Translate().RawText;
        return text.CanTranslate() ? text.Translate().RawText : text;
    }
}
