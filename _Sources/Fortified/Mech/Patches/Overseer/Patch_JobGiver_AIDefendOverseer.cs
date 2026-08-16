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
	[HarmonyPatch(typeof(JobGiver_AIDefendOverseer), "GetDefendee")]
	public static class Patch_JobGiver_AIDefendOverseer
	{
		[HarmonyPostfix]
		public static void Postfix(Pawn pawn, ref Pawn __result)
		{
			if (pawn is IOverseer)
			{
				__result = pawn;
				return;
			}
			if (__result == null) return;
			Pawn overseer = OverseerUtility.GetOverseerPawn(__result);
			if (overseer != null)
			{
				__result = overseer;
			}
		}
	}
}
