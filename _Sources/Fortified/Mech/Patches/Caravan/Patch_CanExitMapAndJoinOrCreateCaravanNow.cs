using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Fortified
{
	[HarmonyPatch(typeof(CaravanExitMapUtility), "CanExitMapAndJoinOrCreateCaravanNow")]
	public static class Patch_CanExitMapAndJoinOrCreateCaravanNow
	{
		[HarmonyPostfix]
		public static void Postfix(Pawn pawn, ref bool __result)
		{
			if (__result || !pawn.Spawned)
			{
				return;
			}
			if (!pawn.Map.exitMapGrid.MapUsesExitGrid)
			{
				return;
			}
			if (pawn is ICaravanOwner owner && owner.CanCaravan)
			{
				__result = true;
			}
		}
	}
}
