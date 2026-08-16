using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{
	[HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.CanControlMech))]
	public static class Pawn_MechanitorUtility_CanControlMech
	{
		public static void Postfix(Pawn pawn, Pawn mech, ref AcceptanceReport __result)
		{
			if (!__result.Accepted) return;
			if (mech is IOverseer) __result = false;
		}
	}
}
