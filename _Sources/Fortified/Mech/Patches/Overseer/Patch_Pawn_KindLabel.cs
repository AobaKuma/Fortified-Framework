using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{
	[HarmonyPatch(typeof(Pawn), nameof(Pawn.KindLabel), MethodType.Getter)]
	public static class Patch_Pawn_KindLabel
	{
		public static void Postfix(Pawn __instance, ref string __result)
		{
			Thing overseer = OverseerUtility.GetOverseerThing(__instance);
			if (overseer != null)
			{
				__result = overseer.def.LabelCap;
			}
		}
	}
}
