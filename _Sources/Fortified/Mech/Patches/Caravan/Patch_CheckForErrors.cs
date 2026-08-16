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
	[HarmonyPatch]
	public static class Patch_CheckForErrors
	{
		public static MethodBase TargetMethod()
		{
			return AccessTools.Method(AccessTools.Inner(typeof(Dialog_FormCaravan), "<>c__DisplayClass95_0"), "<CheckForErrors>b__1");
		}

		public static void Postfix(Pawn x, ref bool __result)
		{
			if (!__result)
			{
				__result = x is ICaravanOwner owner && owner.CanCaravan;
			}
		}
	}
}
