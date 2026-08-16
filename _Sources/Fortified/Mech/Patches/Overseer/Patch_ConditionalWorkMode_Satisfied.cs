using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;

namespace Fortified
{
	[HarmonyPatch(typeof(ThinkNode_ConditionalWorkMode), "Satisfied")]
	public static class Patch_ConditionalWorkMode_Satisfied
	{
		[HarmonyPostfix]
		public static void Postfix(Pawn pawn, ref bool __result, ThinkNode_ConditionalWorkMode __instance)
		{
			if (pawn is IOverseerMech mech && mech.WorkMode == __instance.workMode)
			{
				__result = true;
			}
		}
	}
}
