using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Fortified;

/// <summary>
/// 派系戒備等級（DEFCON 類）的一級設定。等級越高，派系行動越頻繁、越激進。
/// One readiness (DEFCON-style) level. Higher levels make the faction act more often and more aggressively.
/// </summary>
public class ReadinessLevel
{
    /// <summary>短標籤，例如「DEFCON III」；可填 Keyed 鍵。Short label (e.g. "DEFCON III"); may be a Keyed key.</summary>
    [MustTranslate] public string label;

    /// <summary>此等級的情勢描述，{0} = 派系名稱；可填 Keyed 鍵。Situation text, {0} = faction name; may be a Keyed key.</summary>
    [MustTranslate] public string description;

    /// <summary>沒有新事件時，多久降一級（天）。Days until the level drops on its own.</summary>
    public FloatRange decayDays = new FloatRange(5f, 10f);

    /// <summary>關掉時此等級不會自行下降（例如最低級）。Off: this level never drops on its own (e.g. the floor).</summary>
    public bool decays = true;

    /// <summary>派系行動的間隔（天）；上限 ≤ 0 表示此等級不行動。Days between faction actions; a max ≤ 0 means none.</summary>
    public FloatRange actionIntervalDays = FloatRange.Zero;

    public bool Acts => actionIntervalDays.max > 0f;

    /// <summary>
    /// 巡邏與增援的節奏倍率（越小越頻繁）。也用來縮放巡邏隊的人數抖動。
    /// Tempo multiplier for patrols and reinforcements (smaller = more often); also scales patrol size spread.
    /// </summary>
    public float tempo = 1f;

    /// <summary>此等級允許同時存在的連隊上限。Maximum companies this level keeps in the field.</summary>
    public int maxCompanies;

    /// <summary>升到此等級時的信件類型；未填時依等級自動選擇。Letter used when rising to this level; picked automatically when empty.</summary>
    public LetterDef riseLetterDef;

    /// <summary>
    /// 每次行動依序執行的動作。每個動作各自擲 chance；exclusive 的動作一旦執行就結束這次行動。
    /// Actions run in order on every action tick. Each rolls its own chance; an exclusive action that fires ends the tick.
    /// </summary>
    public List<ReadinessAction> actions = new List<ReadinessAction>();
}

/// <summary>
/// 派系戒備系統：等級、隨時間下降、事件累積壓力、每級的行動表、宣戰與追緝聯動。
/// 執行狀態在 <see cref="GameComponent_Readiness"/>，操作一律透過 <see cref="ReadinessUtility"/>。
///
/// Faction readiness (DEFCON) system: levels that decay over time, pressure built up by events, a per-level action
/// table, war campaigns and an optional huntdown. Runtime state lives in <see cref="GameComponent_Readiness"/>;
/// drive it through <see cref="ReadinessUtility"/>.
/// </summary>
public class ReadinessDef : Def
{
    public FactionDef faction;

    public Type workerClass = typeof(ReadinessWorker);

    /// <summary>索引即等級，0 為最低。Index is the level; 0 is the lowest.</summary>
    public List<ReadinessLevel> levels = new List<ReadinessLevel>();

    public int startingLevel;

    /// <summary>累積多少壓力升一級。Pressure needed to rise one level.</summary>
    public float pressurePerLevel = 1f;

    /// <summary>降級時是否清空累積中的壓力。Whether a drop clears the pressure built up so far.</summary>
    public bool dropClearsPressure = true;

    // ── 連隊 Companies ─────────────────────────────────────────────────────

    /// <summary>
    /// 部署連隊時建立的世界物件（worldObjectClass 須為 FFF_Company 或其子類別）。未填時不部署連隊。
    /// World object made when deploying a company (its class must be FFF_Company or a subclass). Empty: no companies.
    /// </summary>
    public WorldObjectDef companyWorldObject;

    // ── 戰爭 War campaign ──────────────────────────────────────────────────

    /// <summary>達到此等級時向一個敵對派系宣戰；-1 不宣戰。Level at which a war is declared on a rival faction; -1 never.</summary>
    public int warMinLevel = -1;

    /// <summary>戰爭結束（目標派系覆滅）後降到的等級。Level the faction falls back to once the war target is defeated.</summary>
    public int levelAfterWar = 2;

    /// <summary>只有高於此等級才會在戰爭結束時發信並降級。Only above this level does a war's end send a letter and drop the level.</summary>
    public int warOverMinLevel = 2;

    public bool warTargetsHumanlikeOnly = true;

    /// <summary>接管聚落時發信的機率（接管很常見，全發會洗版）。Chance to send a letter for a takeover; they are common.</summary>
    public float takeoverLetterChance = 0.25f;

    // ── 永久敵對 Permanent hostility ───────────────────────────────────────

    /// <summary>
    /// 玩家把戒備推到此等級時，派系對玩家永久敵對（<see cref="FactionHostilityLock"/>，不可逆）；-1 不鎖。
    /// When the player pushes readiness to this level the faction turns permanently hostile (<see cref="FactionHostilityLock"/>,
    /// irreversible); -1 never locks.
    /// </summary>
    public int permanentHostilityLevel = -1;

    /// <summary>
    /// 不算「玩家造成」的升級原因：派系自行升溫（action）、時間降級（decay）、戰爭結束（warOver）。
    /// Rise reasons that aren't the player's doing: the faction heating up on its own, decay and war end.
    /// </summary>
    public List<string> permanentHostilityIgnoredReasons = new List<string> { "action", "decay", "warOver" };

    /// <summary>{0} = 派系名稱；可填 Keyed 鍵。{0} = faction name; may be a Keyed key.</summary>
    [MustTranslate] public string permanentHostilityLetterLabel;
    [MustTranslate] public string permanentHostilityLetterText;

    // ── 追緝 Huntdown ─────────────────────────────────────────────────────

    /// <summary>達到 huntdownMinLevel 時啟動、低於時停止的追緝。Huntdown started at huntdownMinLevel and stopped below it.</summary>
    public HuntdownDef huntdown;
    public int huntdownMinLevel = int.MaxValue;

    // ── 介面 UI ───────────────────────────────────────────────────────────

    public bool showAlert = true;

    /// <summary>右側提示出現的最低等級。Lowest level that shows the alert.</summary>
    public int alertMinLevel = 1;

    public bool sendLevelLetters = true;

    // ── 文字 Text：皆可填 Keyed 鍵或直接文字。{0} = 派系名稱，{1} = 等級標籤 ─────────────
    // Text: each may be a Keyed key or literal text. {0} = faction name, {1} = level label.

    [MustTranslate] public string riseLetterLabel;
    [MustTranslate] public string riseLetterText;
    [MustTranslate] public string dropLetterLabel;
    [MustTranslate] public string dropLetterText;
    [MustTranslate] public string alertLabel;
    [MustTranslate] public string alertExplanation;

    /// <summary>{0} = 本派系，{1} = 目標派系。{0} = this faction, {1} = target faction.</summary>
    [MustTranslate] public string warLetterLabel;
    [MustTranslate] public string warLetterText;
    [MustTranslate] public string warOverLetterLabel;
    [MustTranslate] public string warOverLetterText;

    /// <summary>{0} = 本派系，{1} = 原派系，{2} = 舊名，{3} = 新名。{0} = us, {1} = former owner, {2} = old name, {3} = new name.</summary>
    [MustTranslate] public string takeoverLetterLabel;
    [MustTranslate] public string takeoverLetterText;

    [Unsaved] private ReadinessWorker workerInt;

    public ReadinessWorker Worker
    {
        get
        {
            if (workerInt == null)
            {
                workerInt = (ReadinessWorker)Activator.CreateInstance(workerClass);
                workerInt.def = this;
            }
            return workerInt;
        }
    }

    public int MaxLevel => levels.Count - 1;

    public ReadinessLevel LevelAt(int level) => levels[UnityEngine.Mathf.Clamp(level, 0, MaxLevel)];

    public bool HasWar => warMinLevel >= 0;

    public string RiseLetterLabel => ReadinessUtility.KeyOrText(riseLetterLabel, "FFF_Readiness_RiseLabel");
    public string RiseLetterText => ReadinessUtility.KeyOrText(riseLetterText, "FFF_Readiness_RiseText");
    public string DropLetterLabel => ReadinessUtility.KeyOrText(dropLetterLabel, "FFF_Readiness_DropLabel");
    public string DropLetterText => ReadinessUtility.KeyOrText(dropLetterText, "FFF_Readiness_DropText");
    public string AlertLabel => ReadinessUtility.KeyOrText(alertLabel, "FFF_Readiness_AlertLabel");
    public string AlertExplanation => ReadinessUtility.KeyOrText(alertExplanation, "FFF_Readiness_AlertExplanation");
    public string WarLetterLabel => ReadinessUtility.KeyOrText(warLetterLabel, "FFF_Readiness_WarLabel");
    public string WarLetterText => ReadinessUtility.KeyOrText(warLetterText, "FFF_Readiness_WarText");
    public string WarOverLetterLabel => ReadinessUtility.KeyOrText(warOverLetterLabel, "FFF_Readiness_WarOverLabel");
    public string WarOverLetterText => ReadinessUtility.KeyOrText(warOverLetterText, "FFF_Readiness_WarOverText");
    public string TakeoverLetterLabel => ReadinessUtility.KeyOrText(takeoverLetterLabel, "FFF_Readiness_TakeoverLabel");
    public string TakeoverLetterText => ReadinessUtility.KeyOrText(takeoverLetterText, "FFF_Readiness_TakeoverText");
    public string PermanentHostilityLetterLabel => ReadinessUtility.KeyOrText(permanentHostilityLetterLabel, "FFF_Readiness_PermanentHostilityLabel");
    public string PermanentHostilityLetterText => ReadinessUtility.KeyOrText(permanentHostilityLetterText, "FFF_Readiness_PermanentHostilityText");

    /// <summary>等級標籤；未填時用「等級 N」。Level label; "Level N" when empty.</summary>
    public string LevelLabel(int level)
    {
        string text = LevelAt(level).label;
        return text.NullOrEmpty() ? "FFF_Readiness_LevelN".Translate(level).RawText : ReadinessUtility.KeyOrText(text, null);
    }

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors())
        {
            yield return error;
        }
        if (faction == null) yield return "faction is null";
        if (levels.NullOrEmpty())
        {
            yield return "levels is empty";
            yield break;
        }
        if (workerClass == null || !typeof(ReadinessWorker).IsAssignableFrom(workerClass))
        {
            yield return "workerClass must derive from Fortified.ReadinessWorker";
        }
        if (startingLevel < 0 || startingLevel > MaxLevel) yield return "startingLevel is outside levels";
        if (warMinLevel > MaxLevel) yield return "warMinLevel is above the highest level; the war never starts";
        if (huntdown != null && huntdownMinLevel > MaxLevel) yield return "huntdownMinLevel is above the highest level; the huntdown never starts";
        if (pressurePerLevel <= 0f) yield return "pressurePerLevel must be positive";
        if (companyWorldObject != null && !typeof(FFF_Company).IsAssignableFrom(companyWorldObject.worldObjectClass))
        {
            yield return "companyWorldObject's worldObjectClass must be Fortified.FFF_Company or a subclass";
        }
        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] == null)
            {
                yield return $"levels[{i}] is null";
                continue;
            }
            foreach (ReadinessAction action in levels[i].actions ?? new List<ReadinessAction>())
            {
                if (action == null) { yield return $"levels[{i}] has a null action"; continue; }
                foreach (string error in action.ConfigErrors())
                {
                    yield return $"levels[{i}] {action.GetType().Name}: {error}";
                }
            }
        }
    }
}
