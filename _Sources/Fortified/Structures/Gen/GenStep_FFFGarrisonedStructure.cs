using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Fortified.Structures
{
    /// <summary>
    /// 一圈衛星結構的散布設定。半徑依地圖短邊縮放，所以同一份設定在
    /// 200x200 與 350x350 上會自動給出不同的鬆散度。
    ///
    /// One ring of satellite structures. The radius scales with the map's short side,
    /// so a single spec spreads correctly on a 200x200 and a 350x350 map alike.
    /// </summary>
    public class FFF_SatelliteScatter
    {
        /// <summary>按標籤隨機挑。Random pick by tag.</summary>
        public string tag;

        /// <summary>或直接指定 defName 池。Or an explicit defName pool.</summary>
        public List<string> pool;

        /// <summary>這一圈要放幾座。How many to place in this ring.</summary>
        public IntRange count = new IntRange(1, 1);

        /// <summary>
        /// 環半徑 = 地圖短邊 × radiusPct。0.22 在 250x250 上約 55 格，350x350 上約 77 格。
        /// Ring radius as a fraction of the map's short side.
        /// </summary>
        public float radiusPct = 0.22f;

        /// <summary>半徑上下限（格）。避免小地圖擠成一團、大地圖散到天邊。</summary>
        public float minRadius = 20f;
        public float maxRadius = 9999f;

        /// <summary>逐座的半徑抖動比例，讓環看起來不像量過的。Per-instance radius jitter.</summary>
        public float radiusJitter = 0.12f;

        /// <summary>逐座的角度抖動（占自身扇形的比例）。Angular jitter within each slice.</summary>
        public float angleJitter = 0.35f;

        /// <summary>與核心結構、其他衛星之間至少留幾格。Minimum clear gap.</summary>
        public int minSpacing = 4;

        public bool randomRotation = true;

        /// <summary>&lt;0 表示每張地圖隨機起始角。Negative = random start angle per map.</summary>
        public float startAngle = -1f;

        public IFFF_Structure Resolve()
        {
            if (!pool.NullOrEmpty())
            {
                string choice = pool.RandomElement();
                return (IFFF_Structure)GenDefDatabase.GetDef(typeof(FFF_StructureDef), choice, false)
                    ?? (IFFF_Structure)GenDefDatabase.GetDef(typeof(StructureLayoutDef), choice, false);
            }
            return GenStep_FFFStructureGen.RandomStructureWithTag(tag);
        }
    }

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

        /// <summary>
        /// 外圍衛星結構散布，半徑依地圖尺寸縮放。
        /// 刻意做在 GenStep 而非佈局的 SubStructure 元素裡：只有這裡拿得到 Map，
        /// 而且各座衛星是獨立 Generate，不會撐大主結構的 sketch —— 主結構因此仍
        /// 精準落在地圖中心，ClearConflictArea 與 unfog 也只作用在各自的足跡上。
        ///
        /// Outer satellite scatter, radius scaled to map size. Deliberately here rather
        /// than as SubStructure elements in the layout: only the GenStep sees the Map, and
        /// generating each satellite separately keeps them out of the main structure's
        /// sketch — so the compound stays centred and ClearConflictArea/unfog stay local.
        /// </summary>
        public List<FFF_SatelliteScatter> satelliteScatter;

        public override int SeedPart => 987412365;

        public override void Generate(Map map, GenStepParams parms)
        {
            // IFFF 結構 + 污垢 + SymbolResolvers。
            base.Generate(map, parms);

            IntVec3 center = map.Center;
            Faction faction = map.ParentFaction ?? parms.sitePart?.site?.Faction;

            // 先散布衛星，再放中央件與守軍 —— 這樣 SpawnCenterpiece 的
            // "layout already has one" 檢查看到的是完整地圖狀態。
            // Satellites first, so the centerpiece's "already present" check sees
            // the finished map.
            ScatterSatellites(map, faction);

            SpawnCenterpiece(map, center);
            ScatterDebris(map, center);
            ScatterLoot(map, center, parms);
            if (spawnDefenders)
            {
                SpawnDefenders(map, center, faction, parms);
            }
        }

        /// <summary>
        /// 沿一圈環把衛星結構放出去。每一圈把 count 座平均分到 count 個扇形裡，
        /// 各自帶角度與半徑抖動，避免排成整齊的圓。
        /// 落點必須整個在地圖內、且與核心結構／已放的衛星保持 minSpacing 格；
        /// 試不到位就把半徑往內收 15% 重試，所以小地圖會自動變緊而不是溢出邊界。
        ///
        /// Places each ring's structures one per angular slice, with angle and radius
        /// jitter so the result doesn't read as a drawn circle. A candidate must fit
        /// entirely in bounds and stay minSpacing cells clear of the compound and of
        /// already-placed satellites; failing that the radius shrinks 15% and retries,
        /// so small maps tighten up instead of spilling over the edge.
        /// </summary>
        private void ScatterSatellites(Map map, Faction faction)
        {
            if (satelliteScatter.NullOrEmpty()) return;

            int shortSide = Mathf.Min(map.Size.x, map.Size.z);
            CellRect usable = CellRect.WholeMap(map).ContractedBy(2);

            List<CellRect> occupied = new List<CellRect>();
            if (hasLastStructure) occupied.Add(lastStructureRect);

            IntVec3 origin = hasLastStructure ? lastStructureRect.CenterCell : map.Center;

            foreach (FFF_SatelliteScatter spec in satelliteScatter)
            {
                if (spec == null) continue;
                int n = spec.count.RandomInRange;
                if (n <= 0) continue;

                float baseRadius = Mathf.Clamp(shortSide * spec.radiusPct, spec.minRadius, spec.maxRadius);
                float slice = 360f / n;
                float angle0 = spec.startAngle >= 0f ? spec.startAngle : Rand.Range(0f, 360f);

                for (int i = 0; i < n; i++)
                {
                    IFFF_Structure sub = spec.Resolve();
                    if (sub == null)
                    {
                        Log.Warning($"[FortifiedFramework] satelliteScatter: nothing matches tag '{spec.tag}'.");
                        continue;
                    }

                    Rot4 rot = spec.randomRotation ? Rot4.Random : Rot4.North;
                    float radius = baseRadius;
                    bool placed = false;

                    // 半徑逐步內收；每個半徑試 6 次角度抖動。
                    for (int shrink = 0; shrink < 6 && !placed; shrink++, radius *= 0.85f)
                    {
                        for (int attempt = 0; attempt < 6 && !placed; attempt++)
                        {
                            float angle = angle0 + slice * i
                                        + Rand.Range(-slice * spec.angleJitter, slice * spec.angleJitter);
                            float r = radius * (1f + Rand.Range(-spec.radiusJitter, spec.radiusJitter));
                            float rad = angle * Mathf.Deg2Rad;

                            IntVec3 candidate = origin + new IntVec3(
                                Mathf.RoundToInt(Mathf.Cos(rad) * r), 0,
                                Mathf.RoundToInt(Mathf.Sin(rad) * r));

                            CellRect rect = FFF_StructureUtility.FootprintAt(sub, candidate, rot);
                            if (!Contains(usable, rect)) continue;
                            if (occupied.Any(o => OverlapsWithGap(o, rect, spec.minSpacing))) continue;

                            FFF_StructureUtility.Generate(sub, candidate, map, faction, rot, reconnectPower: false);
                            occupied.Add(rect);
                            placed = true;
                        }
                    }

                    if (!placed && Prefs.DevMode)
                    {
                        Log.Message($"[FortifiedFramework] satelliteScatter: no room for " +
                                    $"{(sub as Def)?.defName ?? "structure"} on a {map.Size.x}x{map.Size.z} map; skipped.");
                    }
                }
            }
        }

        /// <summary>
        /// inner 是否完全落在 outer 之內。
        /// CellRect 在此版本沒有 Encapsulates，所以這裡與 OverlapsWithGap 都直接用
        /// minX/maxX/minZ/maxZ 四個邊界欄位算。
        /// （ExpandedBy 與 Area 是存在的 —— CompoundStructureUtility 就在用 ——
        /// 這兩個方法只是為了與 Contains 保持一致的寫法，順帶省掉一次 CellRect 配置。）
        ///
        /// True when <paramref name="inner"/> lies entirely within <paramref name="outer"/>.
        /// CellRect has no Encapsulates in this version, so this and OverlapsWithGap work
        /// off the four edge fields directly. (ExpandedBy and Area do exist — see
        /// CompoundStructureUtility — these two just stay consistent with Contains and
        /// avoid building a throwaway CellRect.)
        /// </summary>
        private static bool Contains(CellRect outer, CellRect inner)
        {
            return inner.minX >= outer.minX && inner.maxX <= outer.maxX
                && inner.minZ >= outer.minZ && inner.maxZ <= outer.maxZ;
        }

        /// <summary>a 向外擴張 gap 格後是否與 b 相交。Do the rects touch once a is padded by gap?</summary>
        private static bool OverlapsWithGap(CellRect a, CellRect b, int gap)
        {
            return a.minX - gap <= b.maxX && b.minX <= a.maxX + gap
                && a.minZ - gap <= b.maxZ && b.minZ <= a.maxZ + gap;
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
