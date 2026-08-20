using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_MechModificationGizmo
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo gizmo in __result) yield return gizmo;
            if (__instance.Drafted || !MechModificationWindowUtility.CanOpenFor(__instance)) yield break;

            yield return new Command_Action
            {
                defaultLabel = "FFF.MechModification.GizmoLabel".Translate(),
                defaultDesc = "FFF.MechModification.GizmoDesc".Translate(),
                icon = TexCommand.DesirePower,
                action = delegate { MechModificationWindowUtility.OpenFor(__instance); }
            };
        }
    }
}
