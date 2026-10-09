using CombatExtended;
using HarmonyLib;
using Verse;

namespace FortifiedCE
{
    /// <summary>
    /// CE 的 CIWS 砲塔 (Building_Turret_MultiVerbs) 用 activeVerb 記住「這一輪該用哪個 Verb」：
    /// CIWS 找到來襲目標時會設成 CIWS Verb，但只會在自動找目標 (TryFindNewTarget) 或重設強制目標時才清掉。
    /// 玩家在 CIWS 正在交戰時下強制攻擊令，OrderAttack 不會清它，之後整段強制攻擊就被殘留的 CIWS Verb 接手：
    /// CIWS Verb 對地面格子 / pawn 沒有射線，會照預設射線亂打，砲塔頂也轉不到掃射角度 (TurretTop 吃的是 AttackVerb.AimAngleOverride)。
    ///
    /// 強制攻擊的按鈕與射程檢查用的都是主要 Verb (掃射)，所以強制目標有效時，
    /// 若選到的是「沒在連發的 CIWS Verb」就改用主要 Verb。正在連發的 Verb 不動，讓那一輪先打完。
    /// </summary>
    [HarmonyPatch(typeof(Building_Turret_MultiVerbs), nameof(Building_Turret_MultiVerbs.AttackVerb), MethodType.Getter)]
    internal static class Harmony_Turret_MultiVerbs
    {
        public static void Postfix(ref Verb __result, Building_Turret_MultiVerbs __instance)
        {
            if (__result is ITargetSearcher && __result.state != VerbState.Bursting && __instance.ForcedTarget.IsValid)
            {
                Verb primary = __instance.GunCompEq?.PrimaryVerb;
                if (primary != null)
                {
                    __result = primary;
                }
            }
        }
    }
}
