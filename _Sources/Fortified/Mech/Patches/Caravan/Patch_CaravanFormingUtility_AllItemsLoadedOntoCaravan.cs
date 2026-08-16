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
	[HarmonyPatch(typeof(CaravanFormingUtility), nameof(CaravanFormingUtility.AllItemsLoadedOntoCaravan))]
	public static class Patch_CaravanFormingUtility_AllItemsLoadedOntoCaravan
	{
		public static void Postfix(Lord lord, Map map, ref bool __result)
		{
			if (!__result)
			{
				return;
			}
			for (int i = 0; i < lord.ownedPawns.Count; i++)
			{
				if (lord.ownedPawns[i] is ICaravanOwner && lord.ownedPawns[i].mindState.lastJobTag != JobTag.WaitingForOthersToFinishGatheringItems)
				{
					__result = false;
					return;
				}
			}
			IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
			for (int j = 0; j < allPawnsSpawned.Count; j++)
			{
				if (allPawnsSpawned[j].CurJob != null && allPawnsSpawned[j].jobs.curDriver is JobDriver_PrepareCaravan_GatherItems && allPawnsSpawned[j].CurJob.lord == lord)
				{
					__result = false;
					return;
				}
			}
		}
	}

}
