using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{
	[HarmonyPatch(typeof(JobGiver_WanderOverseer), "Target")]
	public static class Patch_JobGiver_WanderOverseer
	{
		[HarmonyPostfix]
		public static void Postfix(Pawn pawn, ref GlobalTargetInfo __result)
		{
			if (pawn is IOverseer)
			{
				__result = pawn;
				return;
			}
			if (__result.Pawn == null) return;
			Thing overseer = OverseerUtility.GetOverseerThing(__result.Pawn);
			if (overseer != null)
			{
				__result = overseer;
			}
		}
	}
}
