using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimWorld;

namespace Fortified
{
	[HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.GetMechGizmos))]
	public static class Patch_MechanitorUtility_GetMechGizmos
	{
		public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn mech)
		{
			foreach (var gizmo in __result)
			{
				if (gizmo is Command_Action command && command.defaultLabel == "CommandSelectOverseer".Translate())
				{
					if (mech is IOverseer)
					{
						continue;
					}
					var overseer = mech.GetOverseer();
					if (overseer != null)
					{
						Thing overlord = overseer.GetOverseerThing(out var overseerInt);
						if (overlord != null)
						{
							command.defaultDesc = "CommandSelectOverseerDesc".Translate();
							command.icon = overseerInt.Comp.SelectIcon;
							command.action = delegate
							{
								Find.Selector.ClearSelection();
								Find.Selector.Select(overlord);
							};
							command.Disabled = !overlord.Spawned;
							command.onHover = delegate
							{
								if (overseer != null)
								{
									if (overlord.Spawned)
									{
										GenDraw.DrawArrowPointingAt(overlord.TrueCenter());
									}
									else if (overlord.SpawnedOrAnyParentSpawned)
									{
										GenDraw.DrawArrowPointingAt(overlord.PositionHeld.ToVector3());
									}
								}
							};
						}
					}
				}
				yield return gizmo;
			}
		}
	}
}
