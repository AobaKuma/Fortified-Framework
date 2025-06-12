﻿using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Fortified
{
    public class ModExtension_DeactivatedMech : DefModExtension
    {
        public float spawnChance = 0.25f;
        public float weaponChance = 0.25f;
        public float innerPawnScatterRange = 0f;
        public Vector3 innerPawnDrawOffset;
        public DrawData weaponDraw = null;
        public FloatRange weaponRandomRotRange = new FloatRange(-1, 1);
        public List<PawnGenOption> possibleGeneratePawn;
        public IntRange damageCount = new IntRange(0, 0);
        public List<HediffDef> damageHediffs = null;
        public IntRange age;
    }
}