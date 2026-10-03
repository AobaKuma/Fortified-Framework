using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Fortified;

/// <summary>
/// 追緝定義：某個派系持續追蹤玩家、在預警後對玩家地圖發動數波襲擊。
/// 由 <see cref="HuntdownUtility"/> 啟動與管理，可來自開局（<see cref="ScenPart_Huntdown"/>）、任務或程式碼。
/// 原型為原版 Odyssey 的 ScenPart_PursuingMechanoids。
///
/// A huntdown: a faction keeps tracking the player and, after a warning, raids the hunted map in waves.
/// Started and managed through <see cref="HuntdownUtility"/>, from a scenario part, a quest or code.
/// Generalised from vanilla Odyssey's ScenPart_PursuingMechanoids.
/// </summary>
public class HuntdownDef : Def
{
    /// <summary>追緝方派系。The hunting faction.</summary>
    public FactionDef faction;

    public Type workerClass = typeof(HuntdownWorker);

    // ── 時程 Timing ─────────────────────────────────────────────────────────

    /// <summary>
    /// 開局地圖（或以 initial 啟動）的預警／首波延遲，比之後的地圖短得多。
    /// Warning / first-wave delay on the starting map (or a map tracked as initial).
    /// </summary>
    public int initialWarningDelay = 2700;
    public int initialRaidDelay = 30000;

    /// <summary>之後追蹤的地圖（例如重力船降落後）使用的隨機延遲。Random delays for maps tracked later.</summary>
    public IntRange warningDelayRange = new IntRange(840000, 960000);
    public IntRange raidDelayRange = new IntRange(1080000, 2100000);

    /// <summary>
    /// 襲擊波次；每波的 delayTicks 從首波時間起算。未填時使用原版的兩波設定。
    /// Raid waves, each delayed from the first-wave tick. Defaults to vanilla's two waves when empty.
    /// </summary>
    public List<HuntdownWave> waves;

    /// <summary>
    /// 最後一波結束後若玩家仍留在該地圖，以 warningDelayRange / raidDelayRange 重新排程。
    /// Reschedule with the random ranges once the last wave has fired, if the map is still hunted.
    /// </summary>
    public bool repeatWhileStaying;

    /// <summary>首波前多久警報轉紅。How long before the first wave the alert turns red.</summary>
    public int criticalAlertLeadTicks = 60000;

    // ── 襲擊 Raid ───────────────────────────────────────────────────────────

    /// <summary>未填時交給 RaidEnemy 依說書人規則挑選。Left to RaidEnemy's own choice when unset.</summary>
    public PawnsArrivalModeDef raidArrivalMode;
    public RaidStrategyDef raidStrategy;

    /// <summary>
    /// 派系目前不敵對（例如軍事法庭休戰）時延後這一波並重新排程；
    /// 否則原版 RaidEnemy 會把非敵對的指定派系換成隨機敵對派系。
    /// Postpone and reschedule a wave while the faction is not hostile (e.g. a court-martial truce);
    /// otherwise vanilla RaidEnemy swaps a non-hostile forced faction for a random hostile one.
    /// </summary>
    public bool postponeWhileNotHostile = true;

    /// <summary>每一波發動後提供的任務（例如軍事法庭）。Quests offered after each wave (e.g. a court-martial).</summary>
    public List<HuntdownQuestOffer> questsOnWave;

    // ── 追蹤條件 Tracking conditions ────────────────────────────────────────

    /// <summary>玩家重力船降落時，追緝跟到新地圖。Follow the player's gravship to each map it lands on.</summary>
    public bool followGravship = true;

    /// <summary>
    /// 沒有任何被追緝的地圖時，自動改追任一玩家據點（追殺整個殖民地，而不是特定地點）。
    /// When no map is hunted, pick up any player home map (hunting the colony rather than one place).
    /// </summary>
    public bool retargetPlayerHome;

    /// <summary>綁定的角色死亡或消失時結束追緝。End the huntdown when its bound pawn dies or is gone.</summary>
    public bool stopWhenBoundPawnLost = true;

    /// <summary>地圖上沒有玩家重力引擎時停止追緝該地圖（原版行為）。Drop a map once it has no player grav engine.</summary>
    public bool requireGravEngine;

    /// <summary>
    /// 地圖上必須存在其中任一物件，否則停止追緝該地圖（例如被追蹤的石碑）。
    /// The map must hold one of these things or it stops being hunted (e.g. the tracked artifact).
    /// </summary>
    public List<ThingDef> requiredThings;

    /// <summary>不追緝的地圖生成器；未填時排除 Mechhive（見 IsExcluded）。Map generators never hunted; Mechhive when unset.</summary>
    public List<MapGeneratorDef> excludedMapGenerators;

    // ── 其他 Misc ──────────────────────────────────────────────────────────

    /// <summary>啟動時對玩家的好感度變化（負值 = 敵對）。Goodwill change applied to the player when started.</summary>
    public int goodwillChangeOnStart;

    /// <summary>預警信件指向的物件，依序找第一個存在的。Things the warning letter points at, first found wins.</summary>
    public List<ThingDef> letterLookTargets;

    public bool sendWarningLetter = true;

    /// <summary>右側倒數警報。The countdown alert.</summary>
    public bool showAlert = true;

    // ── 文字 Text（{0} = 派系名稱 faction name）─────────────────────────────

    [MustTranslate] public string letterLabel;
    [MustTranslate] public string letterText;
    [MustTranslate] public string alertLabel;
    [MustTranslate] public string alertLabelCritical;
    [MustTranslate] public string alertExplanation;
    [MustTranslate] public string alertExplanationCritical;

    [Unsaved] private HuntdownWorker workerInt;

    public HuntdownWorker Worker
    {
        get
        {
            if (workerInt == null)
            {
                workerInt = (HuntdownWorker)Activator.CreateInstance(workerClass);
                workerInt.def = this;
            }
            return workerInt;
        }
    }

    /// <summary>最後一波相對首波的延遲。Delay of the last wave relative to the first.</summary>
    public int LastWaveDelay
    {
        get
        {
            int max = 0;
            foreach (HuntdownWave wave in waves)
            {
                if (wave.delayTicks > max) max = wave.delayTicks;
            }
            return max;
        }
    }

    public override void ResolveReferences()
    {
        base.ResolveReferences();
        if (waves.NullOrEmpty())
        {
            waves = new List<HuntdownWave>
            {
                new HuntdownWave { delayTicks = 0, pointsMultiplier = 1.5f, minPoints = 2000f },
                new HuntdownWave { delayTicks = 30000, pointsMultiplier = 2f, minPoints = 8000f },
            };
        }
        waves.SortBy(w => w.delayTicks);
    }

    // DefOf 與 Keyed 在 ResolveReferences 時不一定就緒，預設值一律在使用時解析。
    // DefOfs and keyed strings may not be ready during ResolveReferences, so defaults resolve on use.

    public bool IsExcluded(MapGeneratorDef generator)
    {
        if (generator == null) return false;
        if (excludedMapGenerators == null) return generator == MapGeneratorDefOf.Mechhive;
        return excludedMapGenerators.Contains(generator);
    }

    public string LetterLabel => TextOrKey(letterLabel, "FFF_Huntdown_LetterLabel");
    public string LetterText => TextOrKey(letterText, "FFF_Huntdown_LetterText");
    public string AlertLabel => TextOrKey(alertLabel, "FFF_Huntdown_AlertLabel");
    public string AlertLabelCritical => TextOrKey(alertLabelCritical, "FFF_Huntdown_AlertLabelCritical");
    public string AlertExplanation => TextOrKey(alertExplanation, "FFF_Huntdown_AlertExplanation");
    public string AlertExplanationCritical => TextOrKey(alertExplanationCritical, "FFF_Huntdown_AlertExplanationCritical");

    private static string TextOrKey(string text, string key)
    {
        return text.NullOrEmpty() ? key.Translate().RawText : text;
    }

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors())
        {
            yield return error;
        }
        if (faction == null)
        {
            yield return "faction is null";
        }
        if (workerClass == null || !typeof(HuntdownWorker).IsAssignableFrom(workerClass))
        {
            yield return "workerClass must derive from Fortified.HuntdownWorker";
        }
        if (initialRaidDelay < initialWarningDelay || raidDelayRange.min < warningDelayRange.max)
        {
            yield return "raid delays should not be shorter than warning delays; the warning would arrive after the raid";
        }
        if (waves != null && waves.Any(w => w.delayTicks < 0))
        {
            yield return "wave delayTicks must not be negative";
        }
        if (questsOnWave != null && questsOnWave.Any(q => q?.quest == null))
        {
            yield return "questsOnWave has an entry without a quest";
        }
    }
}

/// <summary>
/// 波次後提供的任務。已有同一任務待接或進行中時不重複提供，任務自己的 TestRun 不通過時略過。
/// A quest offered after a wave; skipped while one with the same root is pending or ongoing, or when its TestRun fails.
/// </summary>
public class HuntdownQuestOffer
{
    public QuestScriptDef quest;

    public float chance = 1f;

    /// <summary>用據點任務的威脅點數（而非襲擊點數）。Use site threat points instead of raid threat points.</summary>
    public bool siteThreatPoints;
}

/// <summary>一波追緝襲擊。One raid wave of a huntdown.</summary>
public class HuntdownWave
{
    /// <summary>相對首波的延遲。Delay after the first wave.</summary>
    public int delayTicks;

    /// <summary>以地圖當前威脅點數乘上此值。Multiplier on the map's current threat points.</summary>
    public float pointsMultiplier = 1f;

    /// <summary>點數下限。Floor on the raid points.</summary>
    public float minPoints;
}
