// 当白昼倾坠之时
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace Fortified.Structures
{
    public static class FFF_StructureUtility
    {
        /// <param name="reconnectPower">
        /// 是否在結束時強制刷新電網。地圖生成期務必傳 false ——
        /// 生成期間 GenSpawn 的 WipeMode 會摧毀帶 CompPower 的建物，此時強制跑
        /// PowerNetManager.UpdatePowerNetsAndConnections_First()，它會對已 despawn 的
        /// 連接器呼叫 TryConnectToAnyPowerNet，parent.Map 已是 null → NRE。
        /// 生成結束後遊戲自己會把電網建好，只有執行期即時生成（除錯生成等）才需要 true。
        ///
        /// Whether to force a power-net refresh at the end. Pass false during map
        /// generation: GenSpawn's WipeMode destroys CompPower buildings while generating,
        /// and forcing UpdatePowerNetsAndConnections_First() then makes it call
        /// TryConnectToAnyPowerNet on already-despawned connectors whose parent.Map is
        /// null, throwing an NRE. The game builds the nets itself once generation ends;
        /// only runtime spawning (debug placement and the like) needs true.
        /// </param>
        public static void Generate(IFFF_Structure def, IntVec3 center, Map map, Faction faction = null, Rot4? rot = null, bool reconnectPower = true)
        {
            if (def == null || map == null) return;

            Rot4 finalRot = rot ?? Rot4.North;

			Sketch sketch = def.GetSketch();
            if (finalRot != Rot4.North) sketch.Rotate(finalRot);

            IntVec3 offset = center - sketch.OccupiedRect.CenterCell;
            CellRect occupiedRect = sketch.OccupiedRect.MovedBy(offset);

            ClearConflictArea(map, occupiedRect);
            SpawnTerrain(map, sketch, offset);
            SpawnThings(map, sketch, offset, faction);
            SpawnPawns(def, finalRot, offset, map, faction);
            HandleRoofs(def, sketch, offset, map, finalRot);
            HandleLegacyLogic(def, offset, map, finalRot);
            FinishGeneration(map, occupiedRect, def, finalRot, offset, reconnectPower);
        }

        /// <summary>
        /// 算出結構若以 center 為中心、依 rot 旋轉後實際會占用的格子範圍。
        /// 用 sketch 的 OccupiedRect 而非 def 宣告的 size —— Generate() 就是這樣定位的，
        /// 且宣告的 size 常與實際元素範圍不一致（例如子結構讓 sketch 溢出宣告尺寸）。
        ///
        /// The cells a structure will actually occupy when centred on <paramref name="center"/>.
        /// Derived from the sketch's OccupiedRect rather than the def's declared size, because
        /// that is what Generate() uses to position it, and the two often disagree.
        /// </summary>
        public static CellRect FootprintAt(IFFF_Structure def, IntVec3 center, Rot4 rot)
        {
            if (def == null) return default;

            Sketch sketch = def.GetSketch();
            if (rot != Rot4.North) sketch.Rotate(rot);
            return sketch.OccupiedRect.MovedBy(center - sketch.OccupiedRect.CenterCell);
        }

		public static void ClearConflictArea(Map map, CellRect rect)
        {
            // 只清理植物、污垢和碎石，建筑由 GenSpawn 的 WipeMode 处理
            foreach (IntVec3 c in rect)
            {
                if (!c.InBounds(map)) continue;
                List<Thing> thingList = c.GetThingList(map).ToList();
                for (int i = 0; i < thingList.Count; i++)
                {
                    Thing t = thingList[i];
                    if (!t.Spawned || !t.def.destroyable) continue;

                    // 1. 只删除植物、污垢、碎石
                    if (
                        //t.def.category == ThingCategory.Plant ||
                        //t.def.category == ThingCategory.Filth ||
                        (t.def.thingCategories != null && t.def.thingCategories.Contains(ThingCategoryDefOf.Chunks)))
                    {
                        t.Destroy(DestroyMode.Vanish);
                    }
                }
            }
        }

        public static void SpawnTerrain(Map map, Sketch sketch, IntVec3 offset)
        {
            foreach (var terrain in sketch.Terrain)
            {
                IntVec3 pos = terrain.pos + offset;
                List<Thing> thingList = pos.GetThingList(map).ToList();
                if (!thingList.NullOrEmpty())
                {
                    for (int i = 0; i < thingList.Count; i++)
                    {
                        Thing t = thingList[i];
                        if (!t.Spawned || !t.def.destroyable) continue;
                        // 删除占位的天然岩石（如果有），以免影响地形生成
                        if (t.def.building?.isNaturalRock ?? false)
                        {
                            t.Destroy(DestroyMode.Vanish);
                        }
                    }
                }
                if (pos.InBounds(map)) map.terrainGrid.SetTerrain(pos, terrain.def);
            }
        }

		public static void SpawnThings(Map map, Sketch sketch, IntVec3 offset, Faction faction)
        {
            var sortedThings = sketch.Things.OrderBy(t => t.SpawnOrder).ToList();

            // 導線／電池／發電機重疊檢查：同一格不能有兩個電力 transmitter，否則
            // PowerNetGrid.RegisterTransmitter 會丟出 "there is already a power net here"。
            // GenSpawn 的 WipeMode 幫不上忙 —— 導線不是 edifice，不會被覆寫掉。
            //
            // 刻意在「還沒生成任何東西」的階段先篩掉落敗者，而不是邊生成邊摧毀已放好的
            // 導線：在生成期摧毀帶 CompPower 的物件，會讓後續的電網刷新對已 despawn 的
            // 連接器呼叫 TryConnectToAnyPowerNet 而 NRE。這裡只動 sketch 清單，不動地圖。
            //
            // Transmitter overlap guard: two power transmitters may never share a cell, or
            // PowerNetGrid.RegisterTransmitter throws "there is already a power net here".
            // WipeMode does not help — conduits aren't edifices.
            //
            // Losers are filtered out before anything spawns rather than by destroying
            // already-placed conduits mid-run: destroying CompPower things during
            // generation makes a later power-net refresh call TryConnectToAnyPowerNet on
            // despawned connectors and throw. This only edits the sketch list.
            var transmitters = new List<(int index, ThingDef def, IntVec3 pos, Rot4 rot)>();
            for (int i = 0; i < sortedThings.Count; i++)
            {
                var t = sortedThings[i];
                if (t.def != null && t.def.EverTransmitsPower)
                    transmitters.Add((i, t.def, t.pos + offset, t.rot));
            }

            HashSet<int> dropIndices = FindTransmitterConflicts(transmitters, map);
            if (dropIndices.Count > 0)
                sortedThings = sortedThings.Where((t, i) => !dropIndices.Contains(i)).ToList();

            if (Prefs.DevMode)
                Log.Message($"[FFF] SpawnThings: trying to generate {sortedThings.Count} things" +
                            (dropIndices.Count > 0 ? $" ({dropIndices.Count} pruned by the transmitter overlap guard)" : ""));

            int spawnedCount = 0;
            foreach (var skThing in sortedThings)
            {
                try
                {
                    IntVec3 pos = skThing.pos + offset;

                    // 边界检查
                    CellRect thingRect = GenAdj.OccupiedRect(pos, skThing.rot, skThing.def.size);
                    if (!thingRect.InBounds(map))
                    {
                        if (Prefs.DevMode) Log.Warning($"[FFF] skip out bound: {skThing.def.defName} at {pos}");
                        continue;
                    }

                    Thing thing = skThing.Instantiate();
                    if (faction != null && thing.def.CanHaveFaction)
                        thing.SetFactionDirect(faction);

                    GenSpawn.Spawn(thing, pos, map, skThing.rot, WipeMode.VanishOrMoveAside);
                    InitializeBuildingState(thing);
                    spawnedCount++;
                }
                catch (Exception e)
                {
                    Log.Error($"[FFF] Error spawning thing {skThing.def.defName}: {e}");
                }
            }

            if (Prefs.DevMode)
                Log.Message($"[FFF] SpawnThings: Successfully generated {spawnedCount} out of {sortedThings.Count} things");
        }

        /// <summary>
        /// 從待生成清單中移除會造成「同格兩個 transmitter」的項目，回傳移除數量。
        /// 完全不動地圖，只縮減傳入的清單。
        ///
        /// 優先度：實體 transmitter（電池、發電機）先占位，導線後占 —— 所以衝突時
        /// 讓位的一定是導線，而不會因為 SpawnOrder 剛好讓導線先跑就犧牲掉電池。
        /// 與地圖上既有 transmitter 衝突時一律放棄新的那個（不摧毀既有建物）。
        ///
        /// Removes entries that would put two power transmitters on one cell; returns how
        /// many were dropped. Touches only the supplied list, never the map.
        ///
        /// Solid transmitters (batteries, generators) claim their cells before conduits do,
        /// so a conflict always costs a conduit rather than sacrificing a battery just
        /// because SpawnOrder happened to run the conduit first. Conflicts against
        /// transmitters already on the map always drop the incoming one.
        /// </summary>
        private static HashSet<int> FindTransmitterConflicts(
            List<(int index, ThingDef def, IntVec3 pos, Rot4 rot)> candidates, Map map)
        {
            HashSet<int> drop = new HashSet<int>();
            if (candidates.NullOrEmpty()) return drop;

            // 非導線優先占位。Solid transmitters claim their cells first.
            candidates.Sort((a, b) => IsConduit(a.def).CompareTo(IsConduit(b.def)));

            HashSet<IntVec3> claimed = new HashSet<IntVec3>();

            foreach (var cand in candidates)
            {
                CellRect rect = GenAdj.OccupiedRect(cand.pos, cand.rot, cand.def.size);

                IntVec3 clash = IntVec3.Invalid;
                string clashWith = null;
                foreach (IntVec3 c in rect)
                {
                    if (claimed.Contains(c))
                    {
                        clash = c; clashWith = "another transmitter in the same layout"; break;
                    }
                    if (MapHasTransmitter(map, c))
                    {
                        clash = c; clashWith = "a transmitter already on the map"; break;
                    }
                }

                if (clashWith != null)
                {
                    drop.Add(cand.index);
                    Log.Warning($"[FFF] Transmitter overlap at {clash}: dropped {cand.def.defName} — " +
                                $"{clashWith} occupies that cell. Two transmitters can't share a cell; " +
                                "fix the structure layout.");
                    continue;
                }

                foreach (IntVec3 c in rect) claimed.Add(c);
            }

            return drop;
        }

        /// <summary>導線類（非 edifice）的 transmitter。Conduit-like, i.e. non-edifice, transmitter.</summary>
        private static bool IsConduit(ThingDef def)
        {
            return def?.building != null && !def.building.isEdifice;
        }

        private static bool MapHasTransmitter(Map map, IntVec3 c)
        {
            if (!c.InBounds(map)) return false;

            List<Thing> things = c.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].Spawned && things[i].def.EverTransmitsPower) return true;
            }
            return false;
        }

		public static void SpawnPawns(IFFF_Structure def, Rot4 rot, IntVec3 offset, Map map, Faction faction)
        {
            var pawns = def.GetPawns(rot, offset);
            if (pawns == null) return;

            foreach (var req in pawns)
            {
                if (!req.Position.InBounds(map)) continue;

                try
                {
                    Faction pawnFaction = faction;
                    if (req.Faction != null) pawnFaction = Find.FactionManager.FirstFactionOfDef(req.Faction);
                    if (pawnFaction == null) pawnFaction = Faction.OfPlayer;

                    Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(req.Kind, pawnFaction, PawnGenerationContext.NonPlayer, -1));
                    if (pawn == null)
                    {
                        Log.Error($"[FFF] Failed to generate pawn: {req.Kind?.defName ?? "null"}");
                        continue;
                    }

                    GenSpawn.Spawn(pawn, req.Position, map, WipeMode.VanishOrMoveAside);

                    if (req.DefendSpawnPoint && pawn.mindState != null)
                        pawn.mindState.duty = new PawnDuty(DutyDefOf.Defend, req.Position, -1f);
                }
                catch (Exception e)
                {
                    Log.Error($"[FFF] Error spawning pawn at {req.Position}: {e}");
                }
            }
        }

		private static void HandleRoofs(IFFF_Structure def, Sketch sketch, IntVec3 offset, Map map, Rot4 rot)
        {
            if (def is StructureLayoutDef layout && !layout.roofGrid.NullOrEmpty())
            {
                ApplyLegacyRoof(layout, offset, map, rot);
            }
            else
            {
                if (def is FFF_StructureDef f && f.disableSuggestedRoof) return;

                foreach (IntVec3 roofCell in sketch.GetSuggestedRoofCells())
                {
                    IntVec3 c = roofCell + offset;
                    if (c.InBounds(map) && !c.Roofed(map))
                        map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                }
            }
        }

        private static void HandleLegacyLogic(IFFF_Structure def, IntVec3 offset, Map map, Rot4 rot)
        {
            if (def is StructureLayoutDef legacy)
                ApplyLegacyTerrainColor(legacy, offset, map, rot);
        }

        private static void FinishGeneration(Map map, CellRect rect, IFFF_Structure def, Rot4 rot, IntVec3 offset, bool reconnectPower)
        {
            foreach (IntVec3 c in rect)
            {
                if (c.InBounds(map)) map.fogGrid.Unfog(c);
            }

            if (reconnectPower) ReconnectPower(map);

            var tasks = def.GetTasks(rot, offset);
            if (tasks != null)
            {
                foreach (var task in tasks)
                {
                    try { task.Execute(map, IntVec3.Zero); }
                    catch (Exception e) { Log.Error($"[FFF] Error executing task {task.GetType().Name}: {e}"); }
                }
            }

            // 结构生成完成后重新计算雾效
            map.fogGrid.Refog(map.BoundsRect());
        }

        private static void InitializeBuildingState(Thing thing)
        {
            // 自动补满燃料
            var refuelable = thing.TryGetComp<CompRefuelable>();
            if (refuelable != null)
            {
                refuelable.Refuel(refuelable.Props.fuelCapacity);
            }

            // 自动补满电量
            var battery = thing.TryGetComp<CompPowerBattery>();
            if (battery != null)
            {
                battery.SetStoredEnergyPct(1f);
            }

            // 应用派系样式与染色
            if (ModsConfig.IdeologyActive && thing.Faction != null && thing.Faction.ideos?.PrimaryIdeo is Ideo ideo)
            {
                thing.SetStyleDef(ideo.GetStyleFor(thing.def));
            }

            if (thing is Building b && b.def.building?.paintable == true)
            {
                // 这里暂时没有从 SymbolDef 传来的颜色信息，但可以在任务中覆盖
            }
        }

        private static void ApplyLegacyRoof(StructureLayoutDef def, IntVec3 offset, Map map, Rot4 rot)
        {
            if (def.roofGrid.NullOrEmpty()) return;

            IntVec2 srcSize = new IntVec2(def.roofGrid[0].Split(',').Length, def.roofGrid.Count);
            IntVec2 rotatedSize = rot.IsHorizontal ? new IntVec2(srcSize.z, srcSize.x) : srcSize;

            for (int z = 0; z < rotatedSize.z; z++)
            {
                for (int x = 0; x < rotatedSize.x; x++)
                {
                    IntVec3 srcPos = GetSourceCoords(x, z, rot, srcSize);
                    string[] cells = def.roofGrid[srcPos.z].Split(',');
                    if (srcPos.x >= cells.Length) continue;

                    string roof = cells[srcPos.x];
                    if (roof == "." || roof.NullOrEmpty()) continue;

                    IntVec3 targetPos = offset + new IntVec3(x, 0, z);
                    if (!targetPos.InBounds(map)) continue;

                    if (roof == "0") // 强制去除
                    {
                        if (def.forceGenerateRoof) map.roofGrid.SetRoof(targetPos, null);
                    }
                    else if (roof == "1") // 构造
                    {
                        if (def.forceGenerateRoof || !targetPos.Roofed(map))
                        {
                            map.roofGrid.SetRoof(targetPos, RoofDefOf.RoofConstructed);
                        }
                    }
                    else if (roof == "2") // 岩石（薄）
                    {
                        if (def.forceGenerateRoof || !targetPos.Roofed(map))
                        {
                            map.roofGrid.SetRoof(targetPos, RoofDefOf.RoofRockThin);
                        }
                    }
                    else if (roof == "3") // 岩石（厚）
                    {
                        map.roofGrid.SetRoof(targetPos, RoofDefOf.RoofRockThick);
                    }
                }
            }
        }

        private static void ApplyLegacyTerrainColor(StructureLayoutDef def, IntVec3 offset, Map map, Rot4 rot)
        {
            if (def.terrainColorGrid.NullOrEmpty()) return;

            IntVec2 srcSize = new IntVec2(def.terrainColorGrid[0].Split(',').Length, def.terrainColorGrid.Count);
            IntVec2 rotatedSize = rot.IsHorizontal ? new IntVec2(srcSize.z, srcSize.x) : srcSize;

            for (int z = 0; z < rotatedSize.z; z++)
            {
                for (int x = 0; x < rotatedSize.x; x++)
                {
                    IntVec3 srcPos = GetSourceCoords(x, z, rot, srcSize);
                    string[] cells = def.terrainColorGrid[srcPos.z].Split(',');
                    if (srcPos.x >= cells.Length || cells[srcPos.x] == ".") continue;

                    var cDef = DefDatabase<ColorDef>.GetNamedSilentFail(cells[srcPos.x]);
                    if (cDef != null)
                    {
                        IntVec3 targetPos = offset + new IntVec3(x, 0, z);
                        if (targetPos.InBounds(map))
                        {
                            map.terrainGrid.SetTerrainColor(targetPos, cDef);
                        }
                    }
                }
            }
        }

        private static IntVec3 GetSourceCoords(int x, int z, Rot4 rot, IntVec2 srcSize)
        {
            switch (rot.AsInt)
            {
                case 0: return new IntVec3(x, 0, z);
                case 1: return new IntVec3(srcSize.x - 1 - z, 0, x);
                case 2: return new IntVec3(srcSize.x - 1 - x, 0, srcSize.z - 1 - z);
                case 3: return new IntVec3(z, 0, srcSize.z - 1 - x);
                default: return IntVec3.Invalid;
            }
        }

        private static void ReconnectPower(Map map)
        {
            map.powerNetManager?.UpdatePowerNetsAndConnections_First();
        }
    }
}
