using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified.Structures
{
    /// <summary>
    /// 套用 StructureAugmentDef：找出符合的結構，依擺放規則挑格子、放物件、接電。
    /// 「朝向」一律指物件實際作用的方向；警報掃描器的 scanRotationOffset（例如牆上攝影機的 180°）會自動換算成物件的 Rotation。
    ///
    /// Applies StructureAugmentDefs: finds the matching structure, picks cells by placement rule, spawns and wires things.
    /// "Facing" always means the direction the thing works toward; an alert scanner's scanRotationOffset (e.g. the 180°
    /// of a wall camera) is converted into the thing's Rotation automatically.
    /// </summary>
    public static class StructureAugmentUtility
    {
        private const int PowerReachSq = 25; // 用電設備會自動連到 5 格內的導電物。Consumers auto-link to transmitters within ~5 cells.

        /// <summary>對剛生成的主結構套用所有符合的增補，回傳放了幾個。Applies every matching augment to a fresh structure; returns how many things were placed.</summary>
        public static int Apply(Map map, CellRect footprint, IFFF_Structure structure, string useTag, Faction faction, GenStepParams parms)
        {
            if (map == null || footprint.Width <= 0 || footprint.Height <= 0) return 0;
            List<StructureAugmentDef> defs = DefDatabase<StructureAugmentDef>.AllDefsListForReading;
            if (defs.Count == 0) return 0;

            HashSet<string> tags = TagsOf(structure);
            if (!useTag.NullOrEmpty()) tags.Add(useTag);
            string structureName = (structure as Def)?.defName;
            float points = parms.sitePart?.parms.threatPoints ?? 0f;
            List<IntVec3> placed = new List<IntVec3>();
            int total = 0;

            foreach (StructureAugmentDef augment in defs)
            {
                if (!Matches(augment, tags, structureName, faction)) continue;
                ReadinessDef readiness = augment.readiness ?? ReadinessUtility.DefFor(faction);
                int level = readiness != null ? ReadinessUtility.LevelOf(readiness) : 0;
                foreach (StructureAugment entry in augment.entries)
                {
                    if (entry?.thing == null || level < entry.minLevel || !Rand.Chance(entry.chance)) continue;
                    int count = CountFor(entry, points, level);
                    if (count <= 0) continue;
                    int done = PlaceEntry(map, footprint, entry, count, faction, placed);
                    total += done;
                    if (Prefs.DevMode && done < count)
                    {
                        Log.Message($"[FortifiedFramework] Augment {augment.defName}: placed {done}/{count} {entry.thing.defName} ({entry.placement}) on {structureName ?? useTag}.");
                    }
                }
            }
            return total;
        }

        public static HashSet<string> TagsOf(IFFF_Structure structure)
        {
            HashSet<string> tags = new HashSet<string>();
            List<string> source = structure switch
            {
                FFF_StructureDef s => s.tags,
                StructureLayoutDef l => l.tags,
                FFF_CompoundStructureDef c => c.tags,
                _ => null,
            };
            if (source != null) tags.UnionWith(source);
            return tags;
        }

        private static bool Matches(StructureAugmentDef augment, HashSet<string> tags, string structureName, Faction faction)
        {
            if (!augment.factions.NullOrEmpty() && (faction == null || !augment.factions.Contains(faction.def))) return false;
            if (!augment.targetStructures.NullOrEmpty() && structureName != null && augment.targetStructures.Contains(structureName)) return true;
            return !augment.targetTags.NullOrEmpty() && augment.targetTags.Any(tags.Contains);
        }

        private static int CountFor(StructureAugment entry, float points, int level)
        {
            int count = entry.countByPoints != null && points > 0f
                ? Mathf.Clamp(GenMath.RoundRandom(entry.countByPoints.Evaluate(points)), entry.count.min, entry.count.max)
                : entry.count.RandomInRange;
            return count + GenMath.RoundRandom(entry.countPerLevel * level);
        }

        // ── 擺放 Placement ─────────────────────────────────────────────────

        private static int PlaceEntry(Map map, CellRect footprint, StructureAugment entry, int count, Faction faction, List<IntVec3> placed)
        {
            ThingDef def = entry.thing;
            AugmentPlacement placement = def.building != null && def.building.isAttachment ? AugmentPlacement.WallMount : entry.placement;
            List<(IntVec3 cell, Rot4 facing)> candidates = Candidates(map, footprint, entry, placement).InRandomOrder().ToList();
            int spacingSq = entry.minSpacing * entry.minSpacing;
            bool indoor = placement == AugmentPlacement.Indoor;
            int done = 0;
            foreach ((IntVec3 cell, Rot4 facing) in candidates)
            {
                if (done >= count) break;
                if (placed.Any(p => (p - cell).LengthHorizontalSquared < spacingSq)) continue;
                Rot4 rot = ThingRotation(def, entry.facing == FFF_FacingMode.None ? Rot4.Random : facing);
                if (!Fits(map, def, cell, rot, indoor, placement == AugmentPlacement.WallMount)) continue;

                Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? entry.stuff ?? GenStuff.DefaultStuffFor(def) : null);
                if (faction != null && def.CanHaveFaction) thing.SetFaction(faction);
                GenSpawn.Spawn(thing, cell, map, rot, WipeMode.Vanish);
                placed.Add(cell);
                done++;
                if (entry.connectPower && thing.TryGetComp<CompPowerTrader>() != null) Wire(map, thing, entry, faction);
            }
            return done;
        }

        /// <summary>候選格與「作用方向」。Candidate cells with the direction the thing should work toward.</summary>
        private static IEnumerable<(IntVec3, Rot4)> Candidates(Map map, CellRect footprint, StructureAugment entry, AugmentPlacement placement)
        {
            IntVec3 center = footprint.CenterCell;
            switch (placement)
            {
                case AugmentPlacement.Perimeter:
                    foreach (IntVec3 c in footprint.ExpandedBy(1).EdgeCells.Concat(footprint.ExpandedBy(2).EdgeCells))
                    {
                        if (c.InBounds(map) && !c.Roofed(map)) yield return (c, Away(c, center));
                    }
                    break;

                case AugmentPlacement.Entrance:
                    foreach (IntVec3 c in footprint.ExpandedBy(1))
                    {
                        if (!c.InBounds(map) || c.GetDoor(map) == null) continue;
                        foreach (Rot4 dir in Rot4.AllRotations)
                        {
                            IntVec3 outside = c + dir.FacingCell;
                            if (!outside.InBounds(map) || outside.Roofed(map)) continue;
                            // 門洞兩旁，不擋門。Beside the doorway, never in front of it.
                            IntVec3 side = dir.RighthandCell;
                            yield return (outside + side, dir);
                            yield return (outside - side, dir);
                        }
                    }
                    break;

                case AugmentPlacement.Indoor:
                    foreach (IntVec3 c in footprint)
                    {
                        if (c.InBounds(map) && c.Roofed(map) && !NextToDoor(map, c)) yield return (c, Away(c, center));
                    }
                    break;

                case AugmentPlacement.WallMount:
                    foreach (IntVec3 c in footprint.ExpandedBy(1))
                    {
                        if (!c.InBounds(map)) continue;
                        foreach (Rot4 dir in Rot4.AllRotations)
                        {
                            IntVec3 behind = c - dir.FacingCell;
                            if (!behind.InBounds(map) || !IsWall(behind.GetEdifice(map))) continue;
                            if (entry.exteriorOnly && c.Roofed(map)) continue;
                            // 面前要有幾格空間，否則夾在兩道牆之間的攝影機什麼也看不到。
                            // Needs room in front; a camera wedged between two walls watches nothing.
                            if (!ClearAhead(map, c, dir, 3)) continue;
                            yield return (c, dir);
                        }
                    }
                    break;

                case AugmentPlacement.Outskirts:
                    CellRect inner = footprint.ExpandedBy(3);
                    foreach (IntVec3 c in footprint.ExpandedBy(Mathf.Max(4, entry.outskirtsRange)))
                    {
                        if (c.InBounds(map) && !inner.Contains(c) && !c.Roofed(map)) yield return (c, Away(c, center));
                    }
                    break;
            }
        }

        /// <summary>依掃描器補正把「作用方向」換成 Rotation。Converts the working direction into Rotation, undoing a scanner's offset.</summary>
        public static Rot4 ThingRotation(ThingDef def, Rot4 facing)
        {
            int offset = def.GetCompProperties<CompProperties_AlertScanner>()?.scanRotationOffset ?? 0;
            return new Rot4(((facing.AsInt - offset) % 4 + 4) % 4);
        }

        private static Rot4 Away(IntVec3 cell, IntVec3 center)
        {
            IntVec3 d = cell - center;
            if (Mathf.Abs(d.x) >= Mathf.Abs(d.z)) return d.x >= 0 ? Rot4.East : Rot4.West;
            return d.z >= 0 ? Rot4.North : Rot4.South;
        }

        private static bool ClearAhead(Map map, IntVec3 c, Rot4 dir, int cells)
        {
            for (int i = 1; i <= cells; i++)
            {
                IntVec3 ahead = c + dir.FacingCell * i;
                if (!ahead.InBounds(map) || ahead.Impassable(map)) return false;
            }
            return true;
        }

        private static bool NextToDoor(Map map, IntVec3 c)
        {
            foreach (IntVec3 n in GenAdj.CellsAdjacent8Way(new TargetInfo(c, map)))
            {
                if (n.InBounds(map) && n.GetDoor(map) != null) return true;
            }
            return false;
        }

        private static bool IsWall(Building b)
        {
            return b != null && b.def.building != null && !b.def.building.isNaturalRock && !(b is Building_Door)
                && b.def.passability == Traversability.Impassable && b.def.fillPercent >= 0.99f && b.def.size == IntVec2.One;
        }

        private static bool Fits(Map map, ThingDef def, IntVec3 cell, Rot4 rot, bool indoor, bool wallMount)
        {
            foreach (IntVec3 c in GenAdj.OccupiedRect(cell, rot, def.size))
            {
                if (!c.InBounds(map)) return false;
                if (c.GetEdifice(map) != null || c.GetFirstBuilding(map) != null) return false;
                TerrainDef terrain = c.GetTerrain(map);
                if (terrain == null || terrain.IsWater || terrain.passability == Traversability.Impassable) return false;
                if (def.terrainAffordanceNeeded != null && !c.SupportsStructureType(map, def.terrainAffordanceNeeded)) return false;
                if (indoor != c.Roofed(map) && !wallMount) return false;
                if (c.GetFirstPawn(map) != null) return false;
            }
            return true;
        }

        // ── 接電 Power ─────────────────────────────────────────────────────

        /// <summary>
        /// 從物件沿最短路徑鋪電線，直到 5 格內有同派系的導電物（用電設備會自己連過去）；找不到就在旁邊放 powerSource。
        /// Lays conduit from the thing along the shortest path until a same-faction transmitter is within 5 cells (consumers
        /// link to it themselves); with none in reach, places powerSource beside it.
        /// </summary>
        private static void Wire(Map map, Thing thing, StructureAugment entry, Faction faction)
        {
            List<IntVec3> transmitters = new List<IntVec3>();
            int radiusSq = entry.powerSearchRadius * entry.powerSearchRadius;
            foreach (Thing b in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                if (b == thing || b.Faction != faction) continue;
                CompPower comp = b.TryGetComp<CompPower>();
                if (comp == null || !comp.Props.transmitsPower) continue;
                if ((b.Position - thing.Position).LengthHorizontalSquared <= radiusSq) transmitters.Add(b.Position);
            }
            if (transmitters.Any(t => (t - thing.Position).LengthHorizontalSquared <= PowerReachSq)) return;

            List<IntVec3> path = transmitters.Count > 0 ? PathToPower(map, thing.Position, transmitters, radiusSq) : null;
            if (path != null)
            {
                foreach (IntVec3 c in path)
                {
                    Thing conduit = ThingMaker.MakeThing(ThingDefOf.PowerConduit);
                    if (faction != null) conduit.SetFaction(faction);
                    GenSpawn.Spawn(conduit, c, map, WipeMode.Vanish);
                }
                return;
            }
            if (entry.powerSource == null) return;
            foreach (IntVec3 c in GenAdj.CellsAdjacentCardinal(thing))
            {
                if (!Fits(map, entry.powerSource, c, Rot4.North, c.Roofed(map), false)) continue;
                Thing source = ThingMaker.MakeThing(entry.powerSource, entry.powerSource.MadeFromStuff ? GenStuff.DefaultStuffFor(entry.powerSource) : null);
                if (faction != null) source.SetFaction(faction);
                GenSpawn.Spawn(source, c, map, WipeMode.Vanish);
                return;
            }
        }

        /// <summary>BFS：從 start 走到離任一導電物 5 格內，回傳要鋪電線的格子（不含已有導電物或會被擠掉東西的格子）。</summary>
        private static List<IntVec3> PathToPower(Map map, IntVec3 start, List<IntVec3> transmitters, int radiusSq)
        {
            Dictionary<IntVec3, IntVec3> parent = new Dictionary<IntVec3, IntVec3> { [start] = start };
            Queue<IntVec3> open = new Queue<IntVec3>();
            open.Enqueue(start);
            while (open.Count > 0)
            {
                IntVec3 c = open.Dequeue();
                if (c != start && transmitters.Any(t => (t - c).LengthHorizontalSquared <= PowerReachSq))
                {
                    List<IntVec3> path = new List<IntVec3>();
                    for (IntVec3 p = c; p != start; p = parent[p]) path.Add(p);
                    return path;
                }
                foreach (IntVec3 dir in GenAdj.CardinalDirections)
                {
                    IntVec3 n = c + dir;
                    if (parent.ContainsKey(n) || !n.InBounds(map) || (n - start).LengthHorizontalSquared > radiusSq) continue;
                    if (!ConduitCanGo(map, n)) continue;
                    parent[n] = c;
                    open.Enqueue(n);
                }
            }
            return null;
        }

        private static bool ConduitCanGo(Map map, IntVec3 c)
        {
            TerrainDef terrain = c.GetTerrain(map);
            if (terrain == null || terrain.IsWater) return false;
            foreach (Thing t in c.GetThingList(map))
            {
                if (t.def == ThingDefOf.PowerConduit || t.TryGetComp<CompPower>() != null) return false;
                if (GenSpawn.SpawningWipes(ThingDefOf.PowerConduit, t.def)) return false;
            }
            return true;
        }
    }
}
