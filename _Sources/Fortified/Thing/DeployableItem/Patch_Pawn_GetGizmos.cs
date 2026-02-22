using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;
using Verse.AI;
using System.Collections.Generic;
using System.Linq;

namespace Fortified
{
    [HarmonyPatch(typeof(Pawn_InventoryTracker), nameof(Pawn_InventoryTracker.GetGizmos))]
    internal static class Patch_Pawn_GetGizmos
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn_InventoryTracker __instance)
        {
            foreach (Gizmo g in __result) yield return g;
            Pawn pawn = __instance.pawn;
			if (pawn.Spawned && Find.Selector.SingleSelectedThing == pawn && pawn.Faction == Faction.OfPlayerSilentFail)
            {
                foreach (Thing thing in __instance.innerContainer)
                {
                    if(thing is IGizmoGiver giver1)
                    {
						Gizmo gizmo1 = giver1.GetGizmoForPawn(pawn);
						if (gizmo1 != null) yield return gizmo1;
					}
                }
                foreach (Thing thing in pawn.equipment.AllEquipmentListForReading)
                {
					if (thing is IGizmoGiver giver2)
					{
						Gizmo gizmo2 = giver2.GetGizmoForPawn(pawn);
						if (gizmo2 != null) yield return gizmo2;
					}
				}
            }
        }
    }
}