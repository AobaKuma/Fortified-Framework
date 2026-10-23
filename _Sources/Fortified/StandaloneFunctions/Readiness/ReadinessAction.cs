using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;

namespace Fortified;

/// <summary>
/// 戒備等級行動表裡的一個動作。在 XML 以 &lt;li Class="..."&gt; 寫進 ReadinessLevel.actions。
/// One entry of a readiness level's action table, written as &lt;li Class="..."&gt; in ReadinessLevel.actions.
/// <code>
/// &lt;actions&gt;
///   &lt;li Class="Fortified.ReadinessAction_RaiseLevel"&gt;&lt;chance&gt;0.05&lt;/chance&gt;&lt;/li&gt;
///   &lt;li Class="Fortified.ReadinessAction_GiveQuest"&gt;&lt;quest&gt;MyQuest&lt;/quest&gt;&lt;chance&gt;0.4&lt;/chance&gt;&lt;exclusive&gt;true&lt;/exclusive&gt;&lt;/li&gt;
///   &lt;li Class="Fortified.ReadinessAction_CompanyOrders"&gt;&lt;perCompany&gt;0.5&lt;/perCompany&gt;&lt;/li&gt;
/// &lt;/actions&gt;
/// </code>
/// </summary>
public abstract class ReadinessAction
{
    public float chance = 1f;

    /// <summary>執行成功後結束這次行動，後面的動作不再跑。Ends the action tick once this one fires.</summary>
    public bool exclusive;

    /// <summary>只在戰爭期間執行。Only while a war is on.</summary>
    public bool requireWar;

    /// <summary>只在派系與玩家敵對時執行。Only while the faction is hostile to the player.</summary>
    public bool requireHostileToPlayer;

    public bool TryRun(ReadinessDef def, ReadinessState state)
    {
        if (requireWar && !state.AtWar) return false;
        Faction faction = def.Worker.Faction;
        if (faction == null) return false;
        if (requireHostileToPlayer && !faction.HostileTo(Faction.OfPlayer)) return false;
        if (!Rand.Chance(chance)) return false;
        return Run(def, state, faction);
    }

    /// <summary>回傳是否真的做了事（exclusive 只在成功時結束行動）。Whether it did anything (exclusive only ends the tick on success).</summary>
    protected abstract bool Run(ReadinessDef def, ReadinessState state, Faction faction);

    public virtual IEnumerable<string> ConfigErrors()
    {
        if (chance <= 0f) yield return "chance is 0; the action never runs";
    }
}

/// <summary>升級（自我升溫）。Raises the level on its own.</summary>
public class ReadinessAction_RaiseLevel : ReadinessAction
{
    public int amount = 1;

    protected override bool Run(ReadinessDef def, ReadinessState state, Faction faction)
    {
        if (state.level >= def.MaxLevel) return false;
        ReadinessUtility.Raise(def, amount, "action");
        return true;
    }
}

/// <summary>降級（自我降溫）。Lowers the level on its own.</summary>
public class ReadinessAction_DropLevel : ReadinessAction
{
    public int amount = 1;

    protected override bool Run(ReadinessDef def, ReadinessState state, Faction faction)
    {
        if (state.level <= 0) return false;
        ReadinessUtility.Drop(def, amount, "action");
        return true;
    }
}

/// <summary>
/// 對玩家提供任務（以玩家據點的威脅點數）。同一任務待接或進行中時不重複。
/// Offers a quest to the player at the home map's threat points; skipped while the same quest is pending or running.
/// </summary>
public class ReadinessAction_GiveQuest : ReadinessAction
{
    public QuestScriptDef quest;
    public float pointsFactor = 1f;
    public bool skipIfActive = true;

    protected override bool Run(ReadinessDef def, ReadinessState state, Faction faction)
    {
        if (quest == null) return false;
        Map home = Find.AnyPlayerHomeMap;
        if (home == null) return false;
        if (skipIfActive && Find.QuestManager.QuestsListForReading.Any(q => q.root == quest
                && (q.State == QuestState.NotYetAccepted || q.State == QuestState.Ongoing)))
        {
            return false;
        }
        Slate slate = new Slate();
        slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(home) * pointsFactor);
        if (!quest.CanRun(slate, home)) return false;
        Quest made = QuestUtility.GenerateQuestAndMakeAvailable(quest, slate);
        if (made == null) return false;
        if (!made.hidden && made.root.sendAvailableLetter) QuestUtility.SendLetterQuestAvailable(made);
        return true;
    }

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors()) yield return error;
        if (quest == null) yield return "quest is null";
    }
}

/// <summary>
/// 對隨機的連隊下令（推進、接管、分兵、夷平、遊走）。總數 = count + perCompany × 連隊數。
/// Orders random companies (advance, take over, split, raze, roam). Total = count + perCompany × company count.
/// </summary>
public class ReadinessAction_CompanyOrders : ReadinessAction
{
    public int count = 1;
    public float perCompany;

    protected override bool Run(ReadinessDef def, ReadinessState state, Faction faction)
    {
        List<FFF_Company> companies = CompanyUtility.CompaniesOf(faction);
        if (companies.Count == 0) return false;
        int total = count + Mathf.FloorToInt(perCompany * companies.Count);
        for (int i = 0; i < total; i++)
        {
            FFF_Company company = companies.RandomElement();
            if (company == null || company.Destroyed) continue;
            company.IssueOrder();
        }
        return total > 0;
    }
}

/// <summary>部署新的連隊；戰爭中朝目標派系，否則朝最近的敵對聚落。Deploys new companies, at the war target or else the nearest hostile settlement.</summary>
public class ReadinessAction_DeployCompanies : ReadinessAction
{
    public int count = 1;

    /// <summary>不超過此等級的連隊上限。Never go past the level's company cap.</summary>
    public bool respectCap = true;

    protected override bool Run(ReadinessDef def, ReadinessState state, Faction faction)
    {
        bool any = false;
        for (int i = 0; i < count; i++)
        {
            if (respectCap && CompanyUtility.CompaniesOf(faction).Count >= state.Current.maxCompanies) break;
            if (CompanyUtility.Deploy(def, state, CompanyUtility.NextObjective(def, state)) == null) break;
            any = true;
        }
        return any;
    }
}

/// <summary>解散連隊；count &lt; 0 全部解散。Disbands companies; count &lt; 0 disbands all.</summary>
public class ReadinessAction_DisbandCompanies : ReadinessAction
{
    public int count = -1;

    protected override bool Run(ReadinessDef def, ReadinessState state, Faction faction)
    {
        List<FFF_Company> companies = CompanyUtility.CompaniesOf(faction);
        if (companies.Count == 0) return false;
        int n = count < 0 ? companies.Count : Mathf.Min(count, companies.Count);
        for (int i = 0; i < n; i++) companies[i].Destroy();
        return n > 0;
    }
}

/// <summary>派一個連隊直接襲擊玩家據點。Sends a company straight at a player home.</summary>
public class ReadinessAction_CompanyRaid : ReadinessAction
{
    public ReadinessAction_CompanyRaid()
    {
        requireHostileToPlayer = true;
    }

    protected override bool Run(ReadinessDef def, ReadinessState state, Faction faction)
    {
        Map map = Find.AnyPlayerHomeMap;
        return map != null && CompanyUtility.RaidMap(map, CompanyUtility.RandomCompanyDef(def, state.level), faction);
    }
}

/// <summary>隨機接管一個敵對（非玩家）聚落；偏好有人形派系的。Takes over a random hostile non-player settlement, humanlike factions first.</summary>
public class ReadinessAction_Takeover : ReadinessAction
{
    protected override bool Run(ReadinessDef def, ReadinessState state, Faction faction)
    {
        List<Settlement> candidates = Find.WorldObjects.SettlementBases
            .Where(s => s.Faction != null && s.Faction != faction && !s.Faction.IsPlayer && s.Faction.HostileTo(faction))
            .ToList();
        if (candidates.Count == 0) return false;
        Settlement target = candidates.Where(s => s.Faction.def.humanlikeFaction).RandomElementWithFallback()
            ?? candidates.RandomElement();
        return def.Worker.Takeover(target) != null;
    }
}

/// <summary>對玩家據點觸發一個事件（例如空襲、轟炸）。Fires an incident on a player home (e.g. an air strike).</summary>
public class ReadinessAction_Incident : ReadinessAction
{
    public IncidentDef incident;
    public float pointsFactor = 1f;

    protected override bool Run(ReadinessDef def, ReadinessState state, Faction faction)
    {
        Map map = Find.AnyPlayerHomeMap;
        if (incident == null || map == null) return false;
        IncidentParms parms = StorytellerUtility.DefaultParmsNow(incident.category, map);
        parms.faction = faction;
        parms.forced = true;
        parms.points *= pointsFactor;
        return incident.Worker.CanFireNow(parms) && incident.Worker.TryExecute(parms);
    }

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors()) yield return error;
        if (incident == null) yield return "incident is null";
    }
}
