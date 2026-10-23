using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Fortified;

/// <summary>
/// 連隊的行為；需要自訂 AI（例如護衛指揮官的 LordJob）或信件時繼承並在 CompanyDef.workerClass 指定。
/// Company behaviour; subclass and set CompanyDef.workerClass for custom AI (e.g. a lord that guards the commander) or letters.
/// </summary>
public class CompanyWorker
{
    public CompanyDef def;

    /// <summary>襲擊玩家時每一排的 Lord。預設：直接進攻殖民地。Lord for each platoon in a raid; defaults to assaulting the colony.</summary>
    public virtual Lord MakeRaidLord(Faction faction, Pawn leader, List<Pawn> platoon, Map map)
    {
        return LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, canKidnap: false, canTimeoutOrFlee: false), map, platoon);
    }

    /// <summary>
    /// 駐地地圖上的守軍 Lord。機械派系守住電力設施，其餘守點；兩者都會主動迎擊從邊緣進來的玩家。
    /// Lord for the camp garrison. Mechanoid factions guard powered buildings, others hold the point; both engage
    /// a player arriving from the edge.
    /// </summary>
    public virtual Lord MakeGarrisonLord(Faction faction, Map map, IntVec3 center)
    {
        if (faction.def.humanlikeFaction)
        {
            return LordMaker.MakeNewLord(faction, new LordJob_DefendPoint(center, 20f, null), map);
        }
        List<Thing> defend = map.listerThings.ThingsInGroup(ThingRequestGroup.PowerTrader);
        return LordMaker.MakeNewLord(faction, new LordJob_MechanoidsDefend(new List<Thing>(defend), faction, 20f, center,
            canAssaultColony: true, isMechCluster: false), map);
    }

    /// <summary>連隊是否仍有指揮官在場；沒有時玩家離開後整個駐地會撤走。Whether a commander remains; without one the camp leaves with the player.</summary>
    public virtual bool HasCommander(Map map, Faction faction)
    {
        foreach (Pawn pawn in map.mapPawns.PawnsInFaction(faction))
        {
            if (!pawn.Dead && def.IsLeaderKind(pawn.kindDef)) return true;
        }
        return false;
    }

    public virtual void SendRaidLetter(Faction faction, string companyName, List<Pawn> leaders)
    {
        Find.LetterStack.ReceiveLetter(def.RaidLetterLabel.Formatted(companyName),
            def.RaidLetterText.Formatted(faction.NameColored, companyName),
            LetterDefOf.ThreatBig, new LookTargets(leaders), faction);
    }
}
