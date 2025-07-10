
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
                if (__instance is Building_TurretCapacityCE building_TurretCapacity && building_TurretCapacity.PawnInside != null)
                {

                    __result = true;
                }
            }
            else if (!__instance.IsMannable) { __result = true; }
        }
    }
}