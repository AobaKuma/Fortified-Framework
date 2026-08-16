using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace Fortified
{
	[HarmonyPatch(typeof(JobGiver_GetEnergy), nameof(JobGiver_GetEnergy.GetMinAutorechargeThreshold))]
	public static class Patch_JobGiver_GetEnergy_Min
	{
		[HarmonyPrefix]
		public static bool Prefix(Pawn pawn, ref int __result)
		{
			if (pawn is IOverseerMech mech)
			{
				int num = pawn.RaceProps.maxMechEnergy;
				__result = Mathf.RoundToInt((float)num * mech.MinCharge);
				return false;
			}
			return true;
		}
	}
}
