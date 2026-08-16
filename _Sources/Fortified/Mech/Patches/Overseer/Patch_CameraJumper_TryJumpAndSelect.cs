using HarmonyLib;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{
	[HarmonyPatch(typeof(CameraJumper), nameof(CameraJumper.TryJumpAndSelect))]
	public static class Patch_CameraJumper_TryJumpAndSelect
	{
		public static void Prefix(ref GlobalTargetInfo target)
		{
			if (target.Thing is Pawn pawn)
			{
				Thing overseer = OverseerUtility.GetOverseerThing(pawn);
				if (overseer != null)
				{
					target = overseer;
				}
			}
		}
	}
}
