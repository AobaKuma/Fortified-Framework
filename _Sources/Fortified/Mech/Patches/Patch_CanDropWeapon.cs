using Verse;
using RimWorld;
using HarmonyLib;
using System.Reflection;

namespace Fortified
{
    [HarmonyPatch(typeof(ITab_Pawn_Gear), "CanControlColonist", MethodType.Getter)]
    public static class Patch_CanDropWeapon
    {
        [HarmonyPostfix]
        public static void CanControl(ref bool __result)
        {
            if (__result) return;
            if (Find.Selector.SingleSelectedThing is Pawn pawn)
            {
                if (pawn is IWeaponUsable && pawn.Faction != null && pawn.Faction.IsPlayer)
                {
                    __result = true;
                }
                if (pawn.TryGetComp<CompDrone>(out var d) && d.CanDraft) __result = true;
            }
        }
    }

	[HarmonyPatch]
	public static class Patch_CanDropWeapon_Sandy_Detailed_RPG_Inventory
	{
		public static MethodBase TargetMethod()
		{
			return AccessTools.Method("Sandy_Detailed_RPG_Inventory.Sandy_Detailed_RPG_GearTab:get_CanControlColonist");
		}

		public static bool Prepare(MethodBase method)
		{
			return AccessTools.Method("Sandy_Detailed_RPG_Inventory.Sandy_Detailed_RPG_GearTab:get_CanControlColonist") != null;
		}

		[HarmonyPostfix]
		public static void Postfix(ref bool __result)
		{
			if (__result) return;
			if (Find.Selector.SingleSelectedThing is Pawn pawn)
            {
                if (pawn is IWeaponUsable && pawn.Faction != null && pawn.Faction.IsPlayer)
                {
                    __result = true;
                }
                if (pawn.TryGetComp<CompDrone>(out var d) && d.CanDraft) __result = true;
            }
		}
	}

	[HarmonyPatch]
	public static class CombatExtended_Utility_Loadouts_IsItemMechanoidWeapon
	{
		public static MethodBase TargetMethod()
		{
			return AccessTools.Method("CombatExtended.Utility_Loadouts:IsItemMechanoidWeapon");
		}

		public static bool Prepare(MethodBase method)
		{
			return AccessTools.Method("CombatExtended.Utility_Loadouts:IsItemMechanoidWeapon") != null;
		}


		[HarmonyPostfix]
		public static void Postfix(Pawn pawn, Thing thing, ref bool __result)
		{
			if (__result == false)
			{
				return;
			}
			if (pawn is IWeaponUsable && pawn.Faction != null && pawn.Faction.IsPlayer && (!pawn.TryGetComp<CompVehicleWeapon>(out var comp) || comp.Props.defaultWeapon == null))
			{
				__result = false;
			}
			else if (pawn.TryGetComp<CompDrone>(out var d) == true && d.CanDraft) __result = false;
		}
	}
}
