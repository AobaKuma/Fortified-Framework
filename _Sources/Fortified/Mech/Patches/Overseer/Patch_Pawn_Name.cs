using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{
	[HarmonyPatch(typeof(Pawn), nameof(Pawn.Name), MethodType.Setter)]
	public static class Patch_Pawn_Name
	{
		[HarmonyPostfix]
		public static void Postfix(Pawn __instance)
		{
			if (__instance is IOverseerMech mech)
			{
				mech.Notify_NameChanged();
			}
		}
	}
}
