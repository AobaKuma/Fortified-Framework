using HarmonyLib;
using RimWorld;
using Verse;

namespace Fortified
{
    [HarmonyPatch(typeof(CompOverseerSubject), "State",MethodType.Getter)]
    public static class Patch_CompOverseerSubject_State
    {
        [HarmonyPostfix]
        public static void Postfix(ref OverseerSubjectState __result, CompOverseerSubject __instance)
        {
            //如果是OverseerSubjectState.Overseen的話就不需要再檢查了。
            if (__result == OverseerSubjectState.Overseen) return;

			//狀態可控機械（IOverseer / AMO…）：單一介面檢查，取代 is IOverseer + woken 雙重判定。
			if (__instance.parent is IStateControllableMech sc && sc.ControllableByState)
			{
				__result = OverseerSubjectState.Overseen;
                return;
			}

			//woken DMS 機械走快取 comps。
			if (__instance.parent.CachedDeadManSwitch()?.woken == true)
            {
                __result = OverseerSubjectState.Overseen;
            }
        }
    }
}
