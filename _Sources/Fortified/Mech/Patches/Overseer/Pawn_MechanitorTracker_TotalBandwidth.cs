using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fortified
{
	[HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.TotalBandwidth), MethodType.Getter)]
	public static class Pawn_MechanitorTracker_TotalBandwidth
	{
		public static void Postfix(ref int __result, Pawn_MechanitorTracker __instance)
		{
			CompOverseer comp = OverseerUtility.GetOverseerComp(__instance.Pawn);
			if (comp == null)
			{
				return;
			}
			if (comp.MechanitorActive)
			{
				__result = comp.CurrentBandwidth;
			}
			else
			{
				__result = 0;
			}
		}
	}
}
