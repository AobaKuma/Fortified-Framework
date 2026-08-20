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
	[HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.Notify_BandwidthChanged))]
	public class Patch_MechanitorTracker_BandwidthChanged
	{
		[HarmonyPrefix]
		public static void Prefix(Pawn_MechanitorTracker __instance)
		{
			//Vanilla notification path - must never throw. GetParentOverseer only
			//resolves for dummy pawns, and the overseer's Comp can still be null.
			__instance?.Pawn?.GetParentOverseer()?.Comp?.RecacheValues();
		}
	}
}
