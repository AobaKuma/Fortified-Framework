using Verse;
using HarmonyLib;
using RimWorld;
using System.Linq;

namespace Fortified
{
    //讓中繼機可以在跟機械師不同地圖時也能控制。

    //此外是讓其他陣營的機械體可以不需要機械師也能受控。

    [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.CanControlMechs), MethodType.Getter)]
    internal static class Patch_CanControlMechs
    {
        internal static void Postfix(Pawn_MechanitorTracker __instance, ref AcceptanceReport __result)
        {
            if (__result == true) return;
            if (__instance.Pawn.HostFaction != null) __result = true;
			Thing overseer = OverseerUtility.GetOverseerThing(__instance.Pawn);
			if (overseer != null && overseer.Spawned)
			{
				__result = true;
                return;
			}
			if (__instance.OverseenPawns?.Any(p => p is ICachedMechComps cc && cc.CommandRelayComp != null) == true) __result = true;
        }
    }
}
