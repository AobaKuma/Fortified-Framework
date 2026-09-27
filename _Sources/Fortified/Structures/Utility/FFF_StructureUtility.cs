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
        /// <summary>
        /// 地圖邊緣安全帶寬度（格）。RimWorld 最外圈是小人進出／撤離與襲擊登場用的，
        /// 結構壓上去會擋住出入口，觀感上也就是「結構生成在地圖邊緣」。
        /// 依地圖短邊 4% 縮放並夾在 8~20 格，讓 200x200 與 400x400 都有合理留白。
        ///
        /// Width of the reserved band along the map edge. RimWorld's outermost ring carries
        /// pawn entry/exit and raid arrival; structures placed there block those paths and
        /// read as "generated on the map border". Scales with 4% of the map's short side,
        /// clamped to 8..20 cells.
        /// </summary>
        public static int EdgeMargin(Map map)
        {
            if (map == null) return 8;
            int shortSide = Mathf.Min(map.Size.x, map.Size.z);
            return Mathf.Clamp(Mathf.RoundToInt(shortSide * 0.04f), 8, 20);
        }

        /// <summary>安全帶以內的可用區域。The usable area inside the edge band.</summary>
        public static CellRect UsableRect(Map map)
        {
            return CellRect.WholeMap(map).ContractedBy(EdgeMargin(map));
        }

        // ── 原版 UsedRects 協定 / Vanilla UsedRects protocol ───────────────────────
        //
        // 1.6 的地標建築（TileMutatorWorker_AncientStructure 等）與原版站點結構都透過
        // MapGenerator 的 "UsedRects" 互相避讓：先落地的把足跡登記進去，後來的
        // MapGenUtility.TryGetStructureRect 就不會挑到重疊的矩形。地標的外圍牆是用
        // GenSpawn.CanSpawnAt(canWipeEdifices:false) 逐格試放的，只要我們的東西壓在那裡，
        // 那一段外牆就直接消失。IFFF 結構過去從不登記，所以在地標地塊上生成站點時，
        // 地標結構會整個疊到我們的營地上，兩邊的外牆都殘缺。
        //
        // Odyssey landmarks (TileMutatorWorker_AncientStructure and friends) and vanilla site
        // structures avoid each other through MapGenerator's "UsedRects": whatever lands first
        // registers its footprint, and MapGenUtility.TryGetStructureRect never picks an
        // overlapping rect. A landmark's perimeter wall is placed cell by cell with
        // GenSpawn.CanSpawnAt(canWipeEdifices:false), so any of our things sitting there simply
        // delete that stretch of wall. IFFF structures never registered, so on a landmark tile the
        // landmark ended up stacked on top of the compound with both perimeters broken.

        private const string UsedRectsVar = "UsedRects";

        /// <summary>
        /// 把足跡登記進 UsedRects（外擴 padding 格，讓地標的外牆與散落物也離遠一點）。
        /// 只在地圖生成期有效；執行期除錯生成沒有 MapGenerator 資料，直接略過。
        /// Registers a footprint (padded) in UsedRects. Only meaningful while the map is being
        /// generated; runtime debug placement has no MapGenerator data and is skipped.
        /// </summary>
        public static void ReserveUsedRect(Map map, CellRect rect, int padding = 0)
        {
            if (map == null || MapGenerator.mapBeingGenerated != map) return;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            List<CellRect> used = MapGenerator.GetOrGenerateVar<List<CellRect>>(UsedRectsVar);
            if (used == null) return;

            CellRect reserved = padding > 0 ? rect.ExpandedBy(padding) : rect;
            used.Add(reserved.ClipInsideMap(map));
        }

        /// <summary>是否與任何已登記的 UsedRect 重疊。Whether the rect overlaps any registered UsedRect.</summary>
        public static bool OverlapsUsedRect(Map map, CellRect rect)
        {
            if (map == null || MapGenerator.mapBeingGenerated != map) return false;
            if (!MapGenerator.TryGetVar<List<CellRect>>(UsedRectsVar, out List<CellRect> used) || used == null) return false;

            for (int i = 0; i < used.Count; i++)
            {
                if (used[i].Overlaps(rect)) return true;
            }
            return false;
        }

        // ── 貨架放置 / Shelf placement ─────────────────────────────────────────

        /// <summary>
        /// 收集矩形內所有已生成的儲物建築（貨架、櫃子……）。
        /// Collects every spawned Building_Storage inside the rect.
        /// </summary>
        public static List<Building_Storage> StoragesIn(Map map, CellRect rect)
        {
            List<Building_Storage> storages = new List<Building_Storage>();
            if (map == null) return storages;
            foreach (IntVec3 c in rect.ClipInsideMap(map))
            {
                List<Thing> things = c.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Building_Storage s && s.Spawned && !storages.Contains(s)) storages.Add(s);
                }
            }
            return storages;
        }

        /// <summary>
        /// 把物品放到清單裡任一收得下它的貨架格位上。Direct 模式會自己處理疊堆與格位上限。
        /// Places the item on any listed storage that accepts it. Direct mode handles stacking and the per-cell cap.
        /// </summary>
        public static bool TryPlaceOnStorages(Thing item, List<Building_Storage> storages, Map map, bool forbidden = true)
        {
            if (item == null || map == null || storages.NullOrEmpty()) return false;
            foreach (Building_Storage storage in storages.InRandomOrder())
            {
                if (!storage.Spawned || !storage.Accepts(item)) continue;
                foreach (IntVec3 cell in storage.AllSlotCells().InRandomOrder())
                {
                    if (!cell.InBounds(map)) continue;
                    if (GenPlace.TryPlaceThing(item, cell, map, ThingPlaceMode.Direct, out Thing placed))
                    {
                        if (forbidden) (placed ?? item).SetForbidden(true, warnOnFail: false);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 收集 rect 內所有已生成的書櫃（Building_Bookcase）。書櫃不是 Building_Storage，StoragesIn 不會收到它。
        /// Collects every spawned Building_Bookcase inside the rect. Bookcases aren't Building_Storage, so StoragesIn misses them.
        /// </summary>
        public static List<Building_Bookcase> BookcasesIn(Map map, CellRect rect)
        {
            List<Building_Bookcase> bookcases = new List<Building_Bookcase>();
            if (map == null) return bookcases;
            foreach (IntVec3 c in rect.ClipInsideMap(map))
            {
                List<Thing> things = c.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Building_Bookcase b && b.Spawned && !bookcases.Contains(b)) bookcases.Add(b);
                }
            }
            return bookcases;
        }

        /// <summary>
        /// 在書櫃裡放 count 本隨機的書（品質隨機），不超過書櫃剩餘的空位。回傳實際放了幾本。
        /// Puts count random books (random quality) into a bookcase, up to its free space. Returns how many went in.
        /// </summary>
        public static int StockBookcase(Building_Bookcase bookcase, int count)
        {
            if (bookcase == null || count <= 0) return 0;
            int placed = 0;
            int failures = 0;
            count = Math.Min(count, bookcase.SpaceRemainingFor(null));
            // 生不出來或收不進去就換一本再試，失敗太多次就收手，不讓壞掉的書卡住整個生成。
            // A book that fails to generate or fit is swapped for another; too many failures and we stop,
            // so one broken book can't stall the whole generation.
            while (placed < count && failures < MaxBookFailures)
            {
                Book book = TryMakeBook(ArtGenerationContext.Outsider);
                if (book == null)
                {
                    failures++;
                    continue;
                }
                if (bookcase.GetDirectlyHeldThings().TryAdd(book)) placed++;
                else
                {
                    book.Destroy();
                    failures++;
                }
            }
            return placed;
        }

        private const int MaxBookFailures = 3;

        /// <summary>
        /// 產生一本隨機的書；失敗時回傳 null 而不是丟例外。書名與內容會跑文法，某個研究或技能的 generalRules 壞掉
        /// （例如 XML 寫成空的 &lt;generalRules /&gt;，RulePack 沒跑 PostLoad）時，非英文語系會在 MakeBook 裡 NRE。
        /// 換一本通常就會抽到別的主題。
        ///
        /// Makes a random book, returning null instead of throwing. Titles and descriptions run through grammar, and a
        /// broken research or skill generalRules (e.g. an empty &lt;generalRules /&gt; in XML, whose RulePack never gets
        /// PostLoad) throws an NRE inside MakeBook under non-English languages. Retrying usually rolls another topic.
        /// </summary>
        public static Book TryMakeBook(ArtGenerationContext context)
        {
            try
            {
                return BookUtility.MakeBook(context);
            }
            catch (Exception ex)
            {
                Log.WarningOnce($"[FFF] Failed to generate a book, skipping it. Some def's grammar rules are likely broken: {ex}", 0x4F46F001);
                return null;
            }
        }

        /// <summary>把一本書收進清單裡任一還有空位的書櫃。Puts a book into any listed bookcase that still has room.</summary>
        public static bool TryPlaceInBookcases(Thing book, List<Building_Bookcase> bookcases)
        {
            if (!(book is Book) || bookcases.NullOrEmpty()) return false;
            foreach (Building_Bookcase bookcase in bookcases.InRandomOrder())
            {
                if (!bookcase.Spawned || !bookcase.Accepts(book)) continue;
                if (bookcase.GetDirectlyHeldThings().TryAdd(book)) return true;
            }
            return false;
        }

        /// <summary>目前登記的 UsedRects 快照（生成期以外回傳空清單）。Snapshot of the registered UsedRects; empty outside generation.</summary>
        public static List<CellRect> CurrentUsedRects(Map map)
        {
            if (map == null || MapGenerator.mapBeingGenerated != map) return new List<CellRect>();
            if (!MapGenerator.TryGetVar<List<CellRect>>(UsedRectsVar, out List<CellRect> used) || used == null) return new List<CellRect>();
            return new List<CellRect>(used);
        }

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
            FinishGeneration(map, occupiedRect, def, finalRot, offset, reconnectPower, faction);
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
                if (pos.InBounds(map))
                {
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
					map.terrainGrid.RemoveTempTerrain(pos, false, true);//should work without check because it checks itself
					map.terrainGrid.SetTerrain(pos, terrain.def);
                }
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

        private static void FinishGeneration(Map map, CellRect rect, IFFF_Structure def, Rot4 rot, IntVec3 offset, bool reconnectPower, Faction faction)
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
                    try { task.Execute(map, IntVec3.Zero, faction); }
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
