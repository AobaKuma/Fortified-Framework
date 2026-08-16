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

	[HarmonyPatch(typeof(SettleInExistingMapUtility), "SettleCommand")]
	public static class Patch_SettleInExistingMapUtility_SettleCommand
	{
		[HarmonyPostfix]
		public static void Postfix(Map map, bool requiresNoEnemies, ref Command __result)
		{
			if (__result.disabledReason == "CommandSettleFailNoColonists".Translate() && map.mapPawns.SpawnedColonyMechs.Any((Pawn x) => x is ICaravanOwner owner && owner.CanCaravan && !x.Downed))
			{
				if (requiresNoEnemies)
				{
					foreach (IAttackTarget item in map.attackTargetsCache.TargetsHostileToColony)
					{
						if (GenHostility.IsActiveThreatToPlayer(item))
						{
							__result.Disable("CommandSettleFailEnemies".Translate());
							return;
						}
					}
				}
				__result.disabledReason = null;
				__result.Disabled = false;
			}
		}
	}
}
