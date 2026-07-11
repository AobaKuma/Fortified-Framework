using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Fortified.Structures
{
    /// <summary>
    /// 駐防結構生成：在 GenStep_FFFStructureGen（結構＋污垢散射）之上，追加：
    /// 1. 保底生成中央建築（例如 Boss 召喚器）
    /// 2. 散落殘骸與戰利品
    /// 3. 生成站點守軍
    /// Garrisoned structure generation: on top of GenStep_FFFStructureGen
    /// (structure + filth), additionally guarantees a centerpiece building
    /// (e.g. a boss caller), scatters debris and loot, and spawns defenders.
    /// </summary>
    public class GenStep_FFFGarrisonedStructure : GenStep_FFFStructureGen
    {
        /// <summary>中央必定生成的建築。Centerpiece building guaranteed to spawn.</summary>
        public ThingDef centerpieceDef;

        /// <summary>殘骸散射。Debris scatter.</summary>
        public ThingDef debrisDef;
        public IntRange debrisCount = new IntRange(8, 14);
        public float scatterRadius = 24f;

        /// <summary>散落戰利品。Scattered loot.</summary>
        public ThingSetMakerDef lootMaker;
        public FloatRange lootMarketValueFallback = new FloatRange(600f, 1000f);

        /// <summary>站點守軍。Site defenders.</summary>
        public bool spawnDefenders = true;
        public float defendRadius = 16f;

        public override int SeedPart => 987412365;

        public override void Generate(Map map, GenStepParams parms)
        {
            // IFFF 結構 + 污垢 + SymbolResolvers。
            base.Generate(map, parms);

            IntVec3 center = map.Center;
            Faction faction = map.ParentFaction ?? parms.sitePart?.site?.Faction;

            SpawnCenterpiece(map, center);
            ScatterDebris(map, center);
            ScatterLoot(map, center, parms);
            if (spawnDefenders)
            {
                SpawnDefenders(map, center, faction, parms);
            }
        }

        private void SpawnCenterpiece(Map map, IntVec3 center)
        {
            if (centerpieceDef == null) return;

            // 若結構佈局內已包含該建築則不再重複生成。
            // Skip if the layout already placed one.
            if (map.listerThings.ThingsOfDef(centerpieceDef).Any()) return;

            if (!TryFindClearCell(map, center, 14, centerpieceDef.Size, out IntVec3 cell))
            {
                cell = CellFinder.RandomCell(map);
                Log.Warning("[FortifiedFramework] GenStep_FFFGarrisonedStructure: no clear cell near center for " + centerpieceDef.defName + ", using random cell.");
            }
            GenSpawn.Spawn(ThingMaker.MakeThing(centerpieceDef), cell, map, Rot4.North);
        }

        private void ScatterDebris(Map map, IntVec3 center)
        {
            if (debrisDef == null) return;
            int count = debrisCount.RandomInRange;
            for (int i = 0; i < count; i++)
            {
                if (CellFinder.TryFindRandomCellNear(center, map, Mathf.RoundToInt(scatterRadius),
                    c => c.Standable(map) && c.GetFirstBuilding(map) == null, out IntVec3 cell))
                {
                    GenSpawn.Spawn(debrisDef, cell, map);
                }
            }
        }

        private void ScatterLoot(Map map, IntVec3 center, GenStepParams parms)
        {
            if (lootMaker == null) return;

            float value = parms.sitePart != null && parms.sitePart.parms.lootMarketValue > 0f
                ? parms.sitePart.parms.lootMarketValue
                : lootMarketValueFallback.RandomInRange;

            ThingSetMakerParams makerParms = default;
            makerParms.totalMarketValueRange = new FloatRange(value * 0.85f, value * 1.15f);
            List<Thing> loot = lootMaker.root.Generate(makerParms);

            foreach (Thing thing in loot)
            {
                if (CellFinder.TryFindRandomCellNear(center, map, Mathf.RoundToInt(scatterRadius),
                    c => c.Standable(map) && c.GetFirstItem(map) == null, out IntVec3 cell))
                {
                    GenSpawn.Spawn(thing, cell, map);
                    thing.SetForbidden(true, warnOnFail: false);
                }
                else
                {
                    thing.Destroy();
                }
            }
        }

        private void SpawnDefenders(Map map, IntVec3 center, Faction faction, GenStepParams parms)
        {
            if (faction == null) return;

            float points = parms.sitePart != null
                ? parms.sitePart.parms.threatPoints
                : StorytellerUtility.DefaultSiteThreatPointsNow();

            PawnGroupMakerParms groupParms = new PawnGroupMakerParms
            {
                groupKind = PawnGroupKindDefOf.Combat,
                tile = map.Tile,
                faction = faction,
                points = Mathf.Max(points, faction.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat)),
                inhabitants = true,
                seed = parms.sitePart != null ? OutpostSitePartUtility.GetPawnGroupMakerSeed(parms.sitePart.parms) : (int?)null
            };

            List<Pawn> pawns = PawnGroupMakerUtility.GeneratePawns(groupParms).ToList();
            if (pawns.Count == 0) return;

            foreach (Pawn pawn in pawns)
            {
                if (!CellFinder.TryFindRandomCellNear(center, map, Mathf.RoundToInt(defendRadius),
                    c => c.Standable(map), out IntVec3 cell))
                {
                    cell = CellFinder.RandomClosewalkCellNear(center, map, Mathf.RoundToInt(defendRadius));
                }
                GenSpawn.Spawn(pawn, cell, map);
            }

            LordMaker.MakeNewLord(faction, new LordJob_DefendPoint(center, defendRadius, null), map, pawns);
        }

        private static bool TryFindClearCell(Map map, IntVec3 center, int maxDist, IntVec2 size, out IntVec3 result)
        {
            return CellFinder.TryFindRandomCellNear(center, map, maxDist, c =>
            {
                CellRect rect = GenAdj.OccupiedRect(c, Rot4.North, size);
                if (!rect.InBounds(map)) return false;
                foreach (IntVec3 rc in rect)
                {
                    if (!rc.Standable(map) || rc.GetEdifice(map) != null) return false;
                }
                return true;
            }, out result);
        }
    }
}
