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
	[HarmonyPatch(typeof(Dialog_FormCaravan), "ShouldShowWarningForMechWithoutMechanitor")]
	public static class Patch_ShouldShowWarningForMechWithoutMechanitor
	{

		private static List<Pawn> tmpPawnsToTransfer = new List<Pawn>();

		[HarmonyPrefix]
		public static bool Prefix(ref bool __result, List<TransferableOneWay> ___transferables)
		{
			foreach (TransferableOneWay transferable in ___transferables)
			{
				if (transferable.HasAnyThing && transferable.AnyThing is IOverseer)
				{
					__result = false;
					return false;
				}
			}
			return true;
		}
	}
}
