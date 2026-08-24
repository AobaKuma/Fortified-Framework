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

			//狀態可控機械（IOverseer / AMO…）：單一介面檢查。
			if (__instance.pawn is IStateControllableMech sc && sc.ControllableByState)
			{
				__result = true;
			}

			//woken DMS 機械走快取 comps。
			if (__instance.pawn is ICachedMechComps cc && cc.DeadManSwitchComp?.woken == true)
            {
                __result = true;
            }
        }
    }
}
