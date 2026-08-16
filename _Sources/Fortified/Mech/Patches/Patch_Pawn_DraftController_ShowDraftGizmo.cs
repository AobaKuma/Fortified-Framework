using HarmonyLib;
using RimWorld;
using Verse;

namespace Fortified
{
    [HarmonyPatch(typeof(Pawn_DraftController), "ShowDraftGizmo",MethodType.Getter)]
    public static class Patch_Pawn_DraftController_ShowDraftGizmo
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result, Pawn_DraftController __instance)
        {
            if(__result) return;

			if (__instance.pawn is IOverseer)
			{
				__result = true;
			}

			if (__instance.pawn.TryGetComp<CompDeadManSwitch>() is CompDeadManSwitch comp && comp.woken)
            {
                __result = true;
            }
        }
    }
}
