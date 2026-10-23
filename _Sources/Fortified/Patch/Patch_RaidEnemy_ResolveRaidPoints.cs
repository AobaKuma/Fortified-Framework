using HarmonyLib;
using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 預設派系的襲擊點數保底。原版 TimedDetectionRaids（限時站點的增援倒數）用站點地圖自己的威脅點數 × 1.5，
    /// 只有商隊在場時常常是 35 × 1.5 = 52.5，低於高單價派系（例如古代軍團）的最低戰鬥編組成本；
    /// 派系又已經指定，結果沒有任何襲擊策略能用（"No raid strategy found"），還會生出空的或不合法的編組。
    /// 派系已定、點數不夠時，把點數拉到該派系能組出一支戰鬥編組的最低值。沒指定派系的襲擊不受影響，
    /// 原版挑派系時本來就會過濾掉組不出隊伍的派系。
    ///
    /// Raid-points floor for raids whose faction is already set. Vanilla TimedDetectionRaids (the timed-site
    /// reinforcement countdown) uses the site map's own threat points × 1.5, which with only a caravan present is
    /// often 35 × 1.5 = 52.5, below the cheapest combat group of an expensive faction (e.g. the Ancient Corps).
    /// With the faction fixed no raid strategy qualifies ("No raid strategy found") and the group comes out empty or
    /// invalid. When the faction is set and the points fall short, raise them to that faction's minimum combat group
    /// cost. Raids without a preset faction are untouched; vanilla's faction pick already filters those.
    /// </summary>
    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "ResolveRaidPoints")]
    public static class Patch_RaidEnemy_ResolveRaidPoints
    {
        public static void Postfix(IncidentParms parms)
        {
            if (parms?.faction == null || parms.faction.def.pawnGroupMakers.NullOrEmpty()) return;
            float min = parms.faction.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat);
            if (min <= 0f || parms.points >= min) return;
            if (Prefs.DevMode)
                Log.Message($"[FortifiedFramework] Raid points {parms.points} below {parms.faction.def.defName}'s minimum combat group; raised to {min}.");
            parms.points = min;
        }
    }
}
