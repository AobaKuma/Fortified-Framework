using Verse;
using HarmonyLib;

namespace Fortified
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.IsColonyMechPlayerControlled), MethodType.Getter)]
    internal static class Patch_IsColonyMechPlayerControlled
    {
        internal static void Postfix(Pawn __instance, ref bool __result)
        {
            if (__result) return;
            if (!__instance.Spawned || !__instance.IsColonyMech) return;
            if (__instance is IWeaponUsable) __result = true;
            if (__instance.CachedDrone() is CompDrone c && c.CanDraft) __result = true;
        }
    }
}
