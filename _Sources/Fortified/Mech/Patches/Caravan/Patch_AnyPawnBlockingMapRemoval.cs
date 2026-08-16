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
	
	[HarmonyPatch(typeof(MapPawns), nameof(MapPawns.AnyPawnBlockingMapRemoval), MethodType.Getter)]
	public class Patch_AnyPawnBlockingMapRemoval
	{
		[HarmonyPostfix]
		public static void Postfix(ref bool __result, MapPawns __instance)
		{
			if (__result) return;
			foreach (Pawn item in __instance.AllPawns)
			{
				if (item is ICaravanOwner owner && owner.CanCaravan)
				{
					__result = true;
					return;
				}
			}
		}
	}
}
