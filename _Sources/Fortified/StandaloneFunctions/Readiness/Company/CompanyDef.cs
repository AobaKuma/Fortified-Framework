using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Fortified;

/// <summary>
/// 一個排（班組）的編制：一名領隊、固定配置、隨機成員。
/// One platoon: a leader, a fixed complement and random members.
/// </summary>
public class PlatoonDef : Def
{
    public List<PawnKindDef> leaderKindDef;

    /// <summary>固定配置；selectionWeight 視為數量（整數）。Fixed complement; selectionWeight is read as a count.</summary>
    public List<PawnGenOption> fixedPawnkind;

    public IntRange memberCountRange;
    public List<PawnKindDef> memberKindDefs;

    public IEnumerable<PawnKindDef> GeneratePawnKinds()
    {
        PawnKindDef leader = leaderKindDef.RandomElementWithFallback();
        if (leader != null) yield return leader;
        if (!fixedPawnkind.NullOrEmpty())
        {
            foreach (PawnGenOption option in fixedPawnkind)
            {
                if (option?.kind == null) continue;
                int count = (int)option.selectionWeight;
                for (int i = 0; i < count; i++) yield return option.kind;
            }
        }
        if (!memberKindDefs.NullOrEmpty())
        {
            int members = memberCountRange.RandomInRange;
            for (int i = 0; i < members; i++)
            {
                PawnKindDef kind = memberKindDefs.RandomElement();
                if (kind != null) yield return kind;
            }
        }
    }

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors()) yield return error;
        if (leaderKindDef.NullOrEmpty()) yield return "no leaderKindDef";
        if (memberKindDefs.NullOrEmpty() && memberCountRange.max > 0) yield return "no memberKindDefs but memberCountRange allows members";
    }
}

/// <summary>
/// 連隊：在世界地圖上移動、圍攻聚落、襲擊玩家的編制部隊。由 <see cref="ReadinessDef"/> 的行動表部署與指揮。
/// A company: an organised force that moves on the world map, besieges settlements and raids the player.
/// Deployed and ordered by a <see cref="ReadinessDef"/>'s action table.
/// </summary>
public class CompanyDef : Def
{
    public FactionDef defaultFaction;
    public Type workerClass = typeof(CompanyWorker);

    public RulePackDef NamePack;
    public IntRange squadCountRange = new IntRange(1, 2);
    public List<PlatoonDef> squads;

    /// <summary>戒備等級達到此值才會部署。Lowest readiness level that deploys this company.</summary>
    public int minLevel;

    [NoTranslate] public string IconPath = "UI/Icons/Company/Default";

    /// <summary>作戰半徑（世界格）：會在半徑內襲擊玩家、圍在目標旁。Combat radius in world tiles.</summary>
    public int combatRadius = 10;

    // ── 指令機率（每次下令依序擲骰）Order odds, rolled in this order on each order ────────

    /// <summary>有目標時按兵不動。Holds position while it has a target.</summary>
    public float holdChance = 0.5f;
    /// <summary>接管目標聚落。Takes the target settlement over.</summary>
    public float takeoverChance = 0.25f;
    /// <summary>分兵：另派一個連隊接手目標，自己轉向。Splits: a new company takes the target over, this one moves on.</summary>
    public float splitChance = 0.25f;
    /// <summary>把目標夷為平地。Razes the target.</summary>
    public float razeChance = 0.1f;
    /// <summary>沒有目標時撤回解散。Disbands when it has no target.</summary>
    public float idleDisbandChance = 0.5f;

    // ── 襲擊 Raids ────────────────────────────────────────────────────────

    /// <summary>從駐地襲擊作戰半徑內玩家據點的冷卻（天）。Cooldown between raids on homes within the combat radius.</summary>
    public float raidCooldownDays = 1.5f;

    /// <summary>{0} = 連隊名稱；可填 Keyed 鍵。{0} = company name; may be a Keyed key.</summary>
    [MustTranslate] public string raidLetterLabel;
    /// <summary>{0} = 派系，{1} = 連隊名稱。{0} = faction, {1} = company name.</summary>
    [MustTranslate] public string raidLetterText;

    [Unsaved] private CompanyWorker workerInt;

    public CompanyWorker Worker
    {
        get
        {
            if (workerInt == null)
            {
                workerInt = (CompanyWorker)Activator.CreateInstance(workerClass);
                workerInt.def = this;
            }
            return workerInt;
        }
    }

    public string RaidLetterLabel => ReadinessUtility.KeyOrText(raidLetterLabel, "FFF_Company_RaidLabel");
    public string RaidLetterText => ReadinessUtility.KeyOrText(raidLetterText, "FFF_Company_RaidText");

    /// <summary>依編制產生整個連隊的兵種。Rolls every pawn kind of the company.</summary>
    public IEnumerable<PawnKindDef> GeneratePawnKinds()
    {
        int squadCount = UnityEngine.Mathf.Max(1, squadCountRange.RandomInRange);
        for (int i = 0; i < squadCount; i++)
        {
            PlatoonDef squad = squads.RandomElement();
            if (squad == null) continue;
            foreach (PawnKindDef kind in squad.GeneratePawnKinds())
            {
                if (kind != null) yield return kind;
            }
        }
    }

    /// <summary>領隊兵種（任何一排的）。Whether the kind leads any of this company's platoons.</summary>
    public bool IsLeaderKind(PawnKindDef kind)
    {
        if (kind == null || squads.NullOrEmpty()) return false;
        for (int i = 0; i < squads.Count; i++)
        {
            if (squads[i]?.leaderKindDef != null && squads[i].leaderKindDef.Contains(kind)) return true;
        }
        return false;
    }

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors()) yield return error;
        if (defaultFaction == null) yield return "defaultFaction is null";
        if (workerClass == null || !typeof(CompanyWorker).IsAssignableFrom(workerClass)) yield return "workerClass must derive from Fortified.CompanyWorker";
        if (squads.NullOrEmpty())
        {
            yield return "squads is empty";
            yield break;
        }
        for (int i = 0; i < squads.Count; i++)
        {
            if (squads[i] == null) yield return $"squads[{i}] is null";
        }
    }
}

/// <summary>
/// 掛在連隊的 WorldObjectDef 上：連隊建立時加入的站點部件。
/// On a company's WorldObjectDef: the site part added when the company is made.
/// </summary>
public class ModExtension_CompanyWorldObject : DefModExtension
{
    public SitePartDef sitePart;
}
