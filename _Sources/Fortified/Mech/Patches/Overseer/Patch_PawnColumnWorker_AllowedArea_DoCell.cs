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
	[HarmonyPatch(typeof(PawnColumnWorker_AllowedArea), nameof(PawnColumnWorker_AllowedArea.DoCell))]
	public static class Patch_PawnColumnWorker_AllowedArea_DoCell
	{
		[HarmonyPrefix]
		public static bool Prefix(Rect rect, Pawn pawn, PawnTable table)
		{
			if (pawn is IOverseer mech)
			{
				if (pawn.playerSettings?.SupportsAllowedAreas == true)
				{
					AreaAllowedGUI.DoAllowedAreaSelectors(rect, pawn);
				}
				return false;
			}
			return true;
		}
	}
}
