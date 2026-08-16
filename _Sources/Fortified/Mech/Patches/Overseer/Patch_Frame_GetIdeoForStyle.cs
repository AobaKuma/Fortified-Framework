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
	[HarmonyPatch(typeof(Frame), "GetIdeoForStyle")]
	public static class Patch_Frame_GetIdeoForStyle
	{
		[HarmonyPrefix]
		public static bool Prefix(Pawn worker, ref Ideo __result)
		{
			if (worker is IOverseer)
			{
				__result = worker.Faction?.ideos?.PrimaryIdeo;
				return false;
			}
			return true;
		}
	}
}
