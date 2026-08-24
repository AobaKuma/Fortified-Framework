using HarmonyLib;
using RimWorld;

namespace Fortified
{
    [HarmonyPatch(typeof(Pawn_StyleTracker), nameof(Pawn_StyleTracker.CanDesireLookChange), MethodType.Getter)]
    public static class Patch_Pawn_StyleTracker_CanDesireLookChange
    {
        public static bool Prefix(Pawn_StyleTracker __instance, ref bool __result)
        {
            if (__instance.pawn is IHumanlikeMech)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
