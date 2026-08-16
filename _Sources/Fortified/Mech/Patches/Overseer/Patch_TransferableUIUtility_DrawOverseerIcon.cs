using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimWorld;
using UnityEngine;

namespace Fortified
{
    [HarmonyPatch(typeof(TransferableUIUtility), "DrawOverseerIcon")]
    public static class Patch_TransferableUIUtility_DrawOverseerIcon
	{
        public static bool Prefix(Pawn overseer, Rect rect)
        {
            Thing overlord = OverseerUtility.GetOverseerThing(overseer);
            if (overlord == null)
            {
                return true;
            }
            GUI.DrawTexture(rect, overlord.def.uiIcon);
            if (!Mouse.IsOver(rect))
            {
                return false;
            }
            Widgets.DrawHighlight(rect);
            TooltipHandler.TipRegion(rect, "MechOverseer".Translate(overseer));
            return false;
        }
    }
}

