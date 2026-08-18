using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;

namespace Fortified
{
	[HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelChanged))]
	public class Patch_ApparelTracker_ApparelChanged
	{
		[HarmonyPostfix]
		public static void Postfix(Pawn_ApparelTracker __instance)
		{
			if (__instance.pawn is IOverseerMech mech)
			{
				mech.Comp.Notify_BandwidthChanged();
			}
		}
	}
}
