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
	[HarmonyPatch(typeof(JobDriver_PrepareCaravan_GatherItems), nameof(JobDriver_PrepareCaravan_GatherItems.IsUsableCarrier))]
	public static class Patch_IsUsableCarrier
	{
		[HarmonyPostfix]
		public static void Postfix(Pawn p, Pawn forPawn, bool allowColonists, ref bool __result)
		{
			if (__result)
			{
				return;
			}
			if (!p.IsFormingCaravan())
			{
				return;
			}
			if (p.DestroyedOrNull() || !p.Spawned || p.inventory.UnloadEverything || !forPawn.CanReach(p, PathEndMode.Touch, Danger.Deadly))
			{
				return;
			}
			if (allowColonists && p is ICaravanOwner owner && owner.CanCaravan)
			{
				__result = true;
			}
		}
	}
}
