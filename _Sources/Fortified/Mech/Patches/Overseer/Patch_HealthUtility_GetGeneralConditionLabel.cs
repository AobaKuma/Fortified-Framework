using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{
	[HarmonyPatch(typeof(HealthUtility), nameof(HealthUtility.GetGeneralConditionLabel))]
	public static class Patch_HealthUtility_GetGeneralConditionLabel
	{
		public static void Postfix(ref string __result, Pawn pawn)
		{
			if (OverseerUtility.GetOverseerPawn(pawn) != null)
			{
				__result = "";
			}
		}
	}
}
