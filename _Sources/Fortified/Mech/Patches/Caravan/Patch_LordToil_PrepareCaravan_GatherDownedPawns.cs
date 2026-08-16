using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Fortified
{
	[HarmonyPatch(typeof(LordToil_PrepareCaravan_GatherDownedPawns), "UpdateAllDuties")]
	public static class Patch_LordToil_PrepareCaravan_GatherDownedPawns
	{
		public static FieldInfo meetingPoint = AccessTools.Field(typeof(LordToil_PrepareCaravan_GatherDownedPawns), "meetingPoint");

		public static FieldInfo exitSpot = AccessTools.Field(typeof(LordToil_PrepareCaravan_GatherDownedPawns), "exitSpot");

		[HarmonyPostfix]
		public static void Postfix(LordToil_PrepareCaravan_GatherDownedPawns __instance)
		{
			for (int i = 0; i < __instance.lord.ownedPawns.Count; i++)
			{
				Pawn pawn = __instance.lord.ownedPawns[i];
				if (pawn is ICaravanOwner)
				{
					pawn.mindState.duty = new PawnDuty(DutyDefOf.PrepareCaravan_GatherDownedPawns, (IntVec3)meetingPoint.GetValue(__instance), (IntVec3)exitSpot.GetValue(__instance));
				}
			}
		}
	}
}
