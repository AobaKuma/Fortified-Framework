
using CombatExtended;
using HarmonyLib;
using UnityEngine;
using Verse;
using Fortified;
namespace FortifiedCE
{
    [HarmonyPatch(typeof(Building_TurretGunCE), "CanSetForcedTarget", MethodType.Getter)]
    internal static class Harmony_TurretGunCE
    {
        public static void Postfix(ref bool __result, Building_TurretGunCE __instance)
        {
            if (__instance.def.HasModExtension<ForceTargetableExtension>())
            {
                if (__instance is Building_TurretCapacityCE building_TurretCapacity)
                {
                    if (building_TurretCapacity.PawnInside != null)
                    {
                        __result = true;
                    }
                }
                // 沒有 Mannable 的一般砲塔 (例如 DMS GateKeeper)：CE 預設 mannableComp != null 才能強制攻擊，
                // 這裡比照 Fortified.Patch_CanSetForcedTarget，玩家陣營的砲塔掛了 ForceTargetableExtension 就可手動指定目標
                else if (!__result && (__instance.Faction == null || __instance.Faction.IsPlayer))
                {
                    __result = true;
                }
            }
            else if (!__instance.IsMannable) { __result = true; }
        }
    }
}