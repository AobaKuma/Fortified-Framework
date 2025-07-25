﻿using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace Fortified
{
    [HarmonyPatch(typeof(SkillRecord))]
    public static class Patch_SkillRecord
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SkillRecord.Learn))]
        public static bool RemoveLearnForMechanoid(Pawn ___pawn)
        {
            return ___pawn is not IWeaponUsable;
        }
        [HarmonyPrefix]
        [HarmonyPriority(501)]
        [HarmonyPatch(nameof(SkillRecord.Interval))]
        public static bool Interval(Pawn ___pawn)
        {
            return ___pawn is not IWeaponUsable;
        }
    }
}
