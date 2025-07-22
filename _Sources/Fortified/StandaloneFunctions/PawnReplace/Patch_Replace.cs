﻿using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Fortified
{
    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.Spawn), new Type[]
    {
    typeof(Thing),    typeof(IntVec3),    typeof(Map),    typeof(Rot4),    typeof(WipeMode),    typeof(bool),
    typeof(bool)
    })]
    public class Patch_Replace
    {
        [HarmonyPrefix]
        public static bool Prefix(ref Thing newThing, IntVec3 loc, Map map, Rot4 rot)
        {
            if (newThing is Pawn pawn && pawn.kindDef?.GetModExtension<ModExtension_ReplacePawn>()
                is ModExtension_ReplacePawn ex && map.Parent is Site site)
            {
                float point = site.ActualThreatPoints;
                if (DebugSettings.godMode) Log.Message($"{site.Label} have {point} ThreatPoints");
                if (ex.replaces.ToList().Find(r => r.Key.Includes(point)) is
                    KeyValuePair<FloatRange, PawnKindDef> replace)
                {
                    if (replace.Value == null)
                    {
                        return false;
                    }
                    newThing = PawnGenerator.GeneratePawn(replace.Value, newThing.Faction, map.Tile);
                }
            }
            return true;
        }
    }
}