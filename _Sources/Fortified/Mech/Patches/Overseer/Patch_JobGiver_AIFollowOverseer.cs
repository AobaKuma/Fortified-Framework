using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{
	[HarmonyPatch(typeof(JobGiver_AIFollowOverseer), "GetFollowee")]
	public static class Patch_JobGiver_AIFollowOverseer
	{
		[HarmonyPostfix]
		public static void Postfix(Pawn pawn, ref Pawn __result)
		{
			if (__result == null) return;
			Pawn mech = OverseerUtility.GetOverseerPawn(__result);
			if (mech != null)
			{
				__result = mech;
			}
		}
	}
}
