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
	[HarmonyPatch(typeof(CaravanExitMapUtility), nameof(CaravanExitMapUtility.ExitMapAndJoinOrCreateCaravan))]
	public static class Patch_ExitMapAndJoinOrCreateCaravan
	{
		[HarmonyPrefix]
		[HarmonyPriority(501)]
		public static bool Prefix(Pawn pawn, Rot4 exitDir)
		{
			if (pawn is ICaravanOwner owner && owner.CanCaravan)
			{
				Caravan caravan = CaravanExitMapUtility.FindCaravanToJoinFor(pawn);
				if (caravan != null)
				{
					//CaravanExitMapUtility.AddCaravanExitTaleIfShould(pawn);
					caravan.AddPawn(pawn, addCarriedPawnToWorldPawnsIfAny: true);
					pawn.ExitMap(allowedToJoinOrCreateCaravan: false, exitDir);
				}
				else
				{
					Map map = pawn.Map;
					PlanetTile directionTile = (PlanetTile)findRandomStartingTileBasedOnExitDir.Invoke(null, new object[2] { map.Tile, exitDir });
					Caravan caravan2 = CaravanExitMapUtility.ExitMapAndCreateCaravan(Gen.YieldSingle(pawn), pawn.Faction, map.Tile, directionTile, PlanetTile.Invalid, sendMessage: false);
					caravan2.autoJoinable = true;
					bool flag = false;
					IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
					for (int i = 0; i < allPawnsSpawned.Count; i++)
					{
						if (CaravanExitMapUtility.FindCaravanToJoinFor(allPawnsSpawned[i]) != null && !allPawnsSpawned[i].Downed && !allPawnsSpawned[i].Drafted)
						{
							if (allPawnsSpawned[i].IsAnimal)
							{
								flag = true;
							}
							RestUtility.WakeUp(allPawnsSpawned[i]);
							allPawnsSpawned[i].jobs.CheckForJobOverride();
						}
					}
					TaggedString taggedString = "MessagePawnLeftMapAndCreatedCaravan".Translate(pawn.LabelShort, pawn).CapitalizeFirst();
					if (flag)
					{
						taggedString += " " + "MessagePawnLeftMapAndCreatedCaravan_AnimalsWantToJoin".Translate();
					}
					Messages.Message(taggedString, caravan2, MessageTypeDefOf.TaskCompletion);
				}
				return false;
			}
			return true;
		}

		public static MethodInfo findRandomStartingTileBasedOnExitDir = AccessTools.Method(typeof(CaravanExitMapUtility), "FindRandomStartingTileBasedOnExitDir", new Type[2] { typeof(PlanetTile), typeof(Rot4) }, (Type[])null);
	}
}
