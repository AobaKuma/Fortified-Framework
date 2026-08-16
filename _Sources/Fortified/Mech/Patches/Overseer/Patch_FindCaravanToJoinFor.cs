using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{
	[HarmonyPatch(typeof(CaravanExitMapUtility), "FindCaravanToJoinFor")]
	public static class Patch_FindCaravanToJoinFor
	{
		[HarmonyPostfix]
		public static void Postfix(Pawn pawn, ref Caravan __result)
		{
			if (__result != null)
			{
				return;
			}
			if (!pawn.IsColonyMech)
			{
				return;
			}
			Pawn overseer = pawn.GetOverseer();
			if (overseer == null || overseer.kindDef != FFF_DefOf.FFF_Dummy)
			{
				return;
			}
			Pawn mech = OverseerUtility.GetOverseerPawn(overseer);
			if (mech == null)
			{
				return;
			}
			if (!pawn.Spawned || !pawn.CanReachMapEdge() || pawn.Map.IsPocketMap)
			{
				return;
			}
			List<PlanetTile> tmpNeighbors = new List<PlanetTile>();
			PlanetTile tile = pawn.Map.Tile;
			Find.WorldGrid.GetTileNeighbors(tile, tmpNeighbors);
			tmpNeighbors.Add(tile);
			List<Caravan> caravans = Find.WorldObjects.Caravans;
			for (int i = 0; i < caravans.Count; i++)
			{
				Caravan caravan = caravans[i];
				if (!tmpNeighbors.Contains(caravan.Tile) || !caravan.autoJoinable)
				{
					continue;
				}
				if (pawn.GetMechWorkMode() == MechWorkModeDefOf.Escort)
				{
					if (caravan.PawnsListForReading.Contains(mech))
					{
						__result = caravan;
					}
				}
			}
		}
	}
}
