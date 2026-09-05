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
	[HarmonyPatch(typeof(CompOverseerSubject), nameof(CompOverseerSubject.CompInspectStringExtra))]
	public class Patch_CompOverseerSubject_CompInspectStringExtra
	{
		[HarmonyPrefix]
		public static bool Prefix(string __result, CompOverseerSubject __instance)
		{
			if(__instance.parent is IStateControllableMech scm && scm.ControllableByState)
			{
				__result = null;
				return false;
			}
			return true;
		}
	}
}
