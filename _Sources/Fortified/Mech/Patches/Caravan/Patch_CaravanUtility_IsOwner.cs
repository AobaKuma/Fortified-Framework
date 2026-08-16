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
	[HarmonyPatch(typeof(CaravanUtility), "IsOwner")]
	public static class Patch_CaravanUtility_IsOwner
	{
		[HarmonyPostfix]
		public static void Postfix(Pawn pawn, Faction caravanFaction, ref bool __result)
		{
			if (__result)
			{
				return;
			}
			if (caravanFaction == null)
			{
				return;
			}
			if (pawn is ICaravanOwner owner && owner.CanCaravan && pawn.Faction == caravanFaction && pawn.HostFaction == null)
			{
				__result = true;
			}
		}
	}
}
