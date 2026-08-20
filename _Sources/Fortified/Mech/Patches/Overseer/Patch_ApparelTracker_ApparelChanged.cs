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
			//Vanilla notification path - must never throw. Comp can be null on a mech
			//that implements IOverseerMech but has no CompOverseer on its def.
			if (__instance?.pawn is IOverseerMech mech)
			{
				mech.Comp?.Notify_BandwidthChanged();
			}
		}
	}
}
