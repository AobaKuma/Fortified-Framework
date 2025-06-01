﻿using Verse;
using HarmonyLib;
using RimWorld;

namespace Fortified
{
    [HarmonyPatch(typeof(PawnRenderNodeWorker_Apparel_Body), nameof(PawnRenderNodeWorker_Apparel_Body.CanDrawNow))]
    public static class Patch_PawnRenderNodeWorker_Apparel_Body_CanDrawNow
    {
        public static bool Prefix(PawnDrawParms parms, ref bool __result)
        {
            return !(parms.pawn is HumanlikeMech) || !(__result = true);
        }
    }
}
