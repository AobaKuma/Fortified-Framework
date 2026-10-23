using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Fortified;

/// <summary>
/// 戒備系統的行為；需要自訂信件、宣戰對象或巡邏反應時繼承並在 ReadinessDef.workerClass 指定。
/// Readiness behaviour; subclass and set ReadinessDef.workerClass to customise letters, war targets or patrol reactions.
/// </summary>
public class ReadinessWorker
{
    public ReadinessDef def;

    public virtual Faction Faction => Find.FactionManager.FirstFactionOfDef(def.faction);

    protected string FactionName => Faction?.NameColored.Resolve() ?? def.faction?.label ?? string.Empty;

    // ── 等級變化 Level changes ─────────────────────────────────────────────

    public virtual void OnLevelChanged(ReadinessState state, int oldLevel, string reason)
    {
        if (def.sendLevelLetters) SendLevelLetter(state, oldLevel);
        if (ShouldLockHostility(state, oldLevel, reason)) LockHostility(state);
        SyncHuntdown(state);
        CheckWar(state);
    }

    public virtual void SendLevelLetter(ReadinessState state, int oldLevel)
    {
        bool rose = state.level > oldLevel;
        string label = rose ? def.RiseLetterLabel : def.DropLetterLabel;
        string text = (rose ? def.RiseLetterText : def.DropLetterText).Formatted(FactionName, def.LevelLabel(state.level)).Resolve();
        string description = LevelDescription(state.level);
        if (!description.NullOrEmpty()) text += "\n\n" + description;
        Find.LetterStack.ReceiveLetter(label.Formatted(FactionName, def.LevelLabel(state.level)), text,
            rose ? RiseLetterDef(state.level) : LetterDefOf.PositiveEvent, lookTargets: null, relatedFaction: Faction);
    }

    /// <summary>此等級的情勢描述。Situation text for a level.</summary>
    public virtual string LevelDescription(int level)
    {
        string text = def.LevelAt(level).description;
        return text.NullOrEmpty() ? string.Empty : ReadinessUtility.KeyOrText(text, null).Formatted(FactionName).Resolve();
    }

    protected virtual LetterDef RiseLetterDef(int level)
    {
        LetterDef set = def.LevelAt(level).riseLetterDef;
        if (set != null) return set;
        if (level >= def.MaxLevel) return LetterDefOf.ThreatBig;
        return level * 5 < def.MaxLevel * 2 ? LetterDefOf.NegativeEvent : LetterDefOf.ThreatSmall;
    }

    // ── 永久敵對 Permanent hostility ───────────────────────────────────────

    /// <summary>
    /// 這次升級是否讓派系對玩家永久敵對：達到 permanentHostilityLevel、確實是升級、而且原因不在忽略清單（不是派系自己升溫）。
    /// 子類別可加上例外（例如帶著特定人物時不鎖）。
    /// Whether this rise locks the faction hostile: it reached permanentHostilityLevel, it really is a rise, and the
    /// reason isn't ignored (not the faction heating up on its own). Subclasses can add exceptions.
    /// </summary>
    public virtual bool ShouldLockHostility(ReadinessState state, int oldLevel, string reason)
    {
        if (def.permanentHostilityLevel < 0 || state.level < def.permanentHostilityLevel || state.level <= oldLevel) return false;
        if (reason != null && def.permanentHostilityIgnoredReasons != null && def.permanentHostilityIgnoredReasons.Contains(reason)) return false;
        return Faction != null && !FactionHostilityLock.IsLocked(Faction);
    }

    public virtual void LockHostility(ReadinessState state)
    {
        Faction faction = Faction;
        if (faction == null) return;
        FactionHostilityLock.Lock(faction, def.PermanentHostilityLetterLabel.Formatted(FactionName).Resolve(),
            def.PermanentHostilityLetterText.Formatted(FactionName).Resolve());
    }

    // ── 行動 Actions ───────────────────────────────────────────────────────

    /// <summary>
    /// 一次行動：先讓連隊數量貼近此等級的上限，再依序跑行動表。
    /// One action tick: bring the company count in line with the level, then run the action table in order.
    /// </summary>
    public virtual void RunActions(ReadinessState state)
    {
        if (Faction == null) return;
        CompanyUtility.MaintainCompanies(def, state);
        List<ReadinessAction> actions = state.Current.actions;
        if (actions.NullOrEmpty()) return;
        for (int i = 0; i < actions.Count; i++)
        {
            int levelBefore = state.level;
            if (actions[i].TryRun(def, state) && (actions[i].exclusive || state.level != levelBefore)) return;
        }
    }

    // ── 戰爭 War ───────────────────────────────────────────────────────────

    /// <summary>檢查戰爭狀態：目標覆滅就結束；達到宣戰等級又沒有目標就宣戰。Ends a finished war; declares one when due.</summary>
    public virtual void CheckWar(ReadinessState state)
    {
        if (!def.HasWar) return;
        if (state.warTarget != null && WarIsWon(state.warTarget))
        {
            EndWar(state);
            return;
        }
        if (state.warTarget == null && state.level >= def.warMinLevel)
        {
            Faction target = ChooseWarTarget();
            if (target != null) DeclareWar(state, target);
        }
    }

    protected virtual bool WarIsWon(Faction target)
    {
        if (target.defeated) return true;
        return !Find.WorldObjects.SettlementBases.Any(s => s.Faction == target);
    }

    /// <summary>預設挑科技等級最高的敵對派系。Defaults to the most advanced hostile faction.</summary>
    public virtual Faction ChooseWarTarget()
    {
        Faction us = Faction;
        if (us == null) return null;
        return Find.FactionManager.AllFactions
            .Where(f => f != us && !f.IsPlayer && !f.Hidden && !f.defeated && f.HostileTo(us)
                && (!def.warTargetsHumanlikeOnly || f.def.humanlikeFaction)
                && Find.WorldObjects.SettlementBases.Any(s => s.Faction == f))
            .OrderByDescending(f => f.def.techLevel)
            .FirstOrDefault();
    }

    public virtual void DeclareWar(ReadinessState state, Faction target)
    {
        state.warTarget = target;
        Find.LetterStack.ReceiveLetter(def.WarLetterLabel.Formatted(FactionName, target.NameColored),
            def.WarLetterText.Formatted(FactionName, target.NameColored), LetterDefOf.NeutralEvent, (LookTargets)null, target);
    }

    public virtual void EndWar(ReadinessState state)
    {
        Faction target = state.warTarget;
        state.warTarget = null;
        if (target == null || state.level <= def.warOverMinLevel) return;
        Find.LetterStack.ReceiveLetter(def.WarOverLetterLabel.Formatted(FactionName, target.NameColored),
            def.WarOverLetterText.Formatted(FactionName, target.NameColored), LetterDefOf.NeutralEvent);
        if (def.levelAfterWar < state.level) ReadinessUtility.SetLevel(def, def.levelAfterWar, "warOver");
    }

    // ── 接管 Takeover ──────────────────────────────────────────────────────

    /// <summary>
    /// 把聚落換成本派系的聚落（原地、改名）。回傳新聚落；失敗時回傳 null。
    /// Replaces a settlement with one of ours in place, renamed. Returns the new settlement, or null.
    /// </summary>
    public virtual Settlement Takeover(Settlement settlement)
    {
        Faction us = Faction;
        if (settlement == null || settlement.Destroyed || us == null || settlement.Faction == null || settlement.Faction == us) return null;
        Faction former = settlement.Faction;
        string oldName = settlement.Name;
        PlanetTile tile = settlement.Tile;
        settlement.Destroy();
        SettlementDefeatUtility.CheckDefeated(settlement);

        Settlement captured = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
        captured.Tile = tile;
        captured.SetFaction(us);
        captured.Name = SettlementNameGenerator.GenerateSettlementName(captured, us.def.settlementNameMaker);
        Find.WorldObjects.Add(captured);
        if (Rand.Chance(def.takeoverLetterChance))
        {
            Find.LetterStack.ReceiveLetter(def.TakeoverLetterLabel.Formatted(FactionName, former.NameColored, oldName, captured.Name),
                def.TakeoverLetterText.Formatted(FactionName, former.NameColored, oldName, captured.Name),
                LetterDefOf.NeutralEvent, captured);
        }
        return captured;
    }

    // ── 追緝 Huntdown ─────────────────────────────────────────────────────

    public virtual void SyncHuntdown(ReadinessState state)
    {
        HuntdownDef huntdown = def.huntdown;
        if (huntdown == null) return;
        if (state.level >= def.huntdownMinLevel)
        {
            Map map = Find.AnyPlayerHomeMap;
            // initial: true 用 def 的短延遲；長延遲（14~35 天）通常比高等級持續的時間還長，第一波前就會被停掉。
            // initial uses the def's short delays; the long ones (14~35 days) usually outlast the level itself.
            if (map != null && !HuntdownUtility.IsActive(huntdown)) HuntdownUtility.Start(huntdown, map, true, def.defName, null);
        }
        else if (HuntdownUtility.IsActive(huntdown))
        {
            HuntdownUtility.Stop(huntdown);
        }
    }

    // ── 外部事件 Outside events ───────────────────────────────────────────

    /// <summary>
    /// 巡邏隊抵達站點地圖。first = 這張地圖的第一支。預設：第一支不做事（子類別可在此宣告敵對），
    /// 之後每支累積 pressure 並提前下一次行動。
    /// A patrol reached a site map; first = the first one on this map. By default the first does nothing (subclasses
    /// can turn hostile here); later ones add pressure and pull the next action forward.
    /// </summary>
    public virtual void OnPatrolArrived(Map map, List<Pawn> pawns, bool first, float pressure)
    {
        if (first) return;
        ReadinessUtility.AddPressure(def, pressure, "patrol");
        ReadinessUtility.Hasten(def);
    }

    /// <summary>玩家攻入連隊駐地（地圖生成時）。Player entered a company camp (on map generation).</summary>
    public virtual void OnCompanyEngaged(FFF_Company company)
    {
        ReadinessUtility.Raise(def, 1, "companyEngaged");
    }
}
