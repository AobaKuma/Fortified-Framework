using Fortified.Structures;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Fortified
{
	/// <summary>
	/// 讓開局以「自訂重力船結構」降落的 ScenPart。
	/// 對應 vanilla 的 ScenPart_PlayerPawnsArriveMethod(Gravship)，但用 FFF 結構取代 SketchGen。
	///
	/// A scenario part that starts the colony on a custom FFF structure instead of the
	/// vanilla generated gravship. Mirrors ScenPart_PlayerPawnsArriveMethod's Gravship path.
	/// </summary>
	public class ScenPart_PlayerPawnsArriveGravship : ScenPart
	{
		private Fortified.Structures.FFF_StructureDef gravshipStructure;

		// 本次地圖生成是否退回投艙開局（不存檔，只供 PostMapGenerate 判斷）。
		// Whether this map generation fell back to drop pods; runtime-only flag for PostMapGenerate.
		private bool usedDropPodFallback;

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Defs.Look(ref gravshipStructure, "gravshipStructure");
		}

		public override void DoEditInterface(Listing_ScenEdit listing)
		{
		}

		public override string Summary(Scenario scen)
		{
			return null;
		}

		public override void Randomize()
		{
		}

		/// <summary>
		/// 這個 ScenPart 只有在結構有效且 Odyssey 啟用時才能走重力船流程。
		/// Whether the gravship path is actually usable.
		/// </summary>
		private bool CanDoGravship
		{
			get
			{
				if (gravshipStructure == null)
				{
					Log.ErrorOnce(
						"[FFF] ScenPart_PlayerPawnsArriveGravship 沒有設定 gravshipStructure，改用投送艙開局。 " +
						"ScenPart_PlayerPawnsArriveGravship has no gravshipStructure set; falling back to drop pods.",
						0x0FF10501);
					return false;
				}
				if (!ModsConfig.OdysseyActive)
				{
					Log.ErrorOnce(
						"[FFF] ScenPart_PlayerPawnsArriveGravship 需要 Odyssey DLC，改用投送艙開局。 " +
						"ScenPart_PlayerPawnsArriveGravship requires Odyssey; falling back to drop pods.",
						0x0FF10502);
					return false;
				}
				return true;
			}
		}

		public override void GenerateIntoMap(Map map)
		{
			usedDropPodFallback = false;
			if (map == null || Find.GameInitData == null)
			{
				return;
			}

			List<Thing> startingItems = CollectStartingItems();

			if (!CanDoGravship)
			{
				usedDropPodFallback = true;
				DoDropPods(map, startingItems);
				return;
			}

			try
			{
				DoGravship(map, startingItems);
			}
			catch (Exception ex)
			{
				Log.Error("[FFF] 重力船開局生成失敗，改用投送艙開局。Gravship start failed, falling back to drop pods: " + ex);
				usedDropPodFallback = true;
				DoDropPods(map, startingItems);
			}
		}

		private static List<Thing> CollectStartingItems()
		{
			List<Thing> list = new List<Thing>();

			Scenario scenario = Find.Scenario;
			if (scenario != null)
			{
				foreach (ScenPart allPart in scenario.AllParts)
				{
					if (allPart == null) continue;
					try
					{
						IEnumerable<Thing> things = allPart.PlayerStartingThings();
						if (things != null) list.AddRange(things.Where(t => t != null));
					}
					catch (Exception ex)
					{
						Log.Error($"[FFF] ScenPart {allPart.GetType().Name} 的 PlayerStartingThings 發生例外：{ex}");
					}
				}
			}

			GameInitData initData = Find.GameInitData;
			List<Pawn> pawns = initData?.startingAndOptionalPawns;
			if (pawns != null && initData.startingPossessions != null)
			{
				foreach (Pawn pawn in pawns)
				{
					if (pawn == null) continue;
					if (!initData.startingPossessions.TryGetValue(pawn, out List<ThingDefCount> possessions) || possessions == null)
					{
						continue;
					}
					foreach (ThingDefCount item in possessions)
					{
						Thing thing = StartingPawnUtility.GenerateStartingPossession(item);
						if (thing != null) list.Add(thing);
					}
				}
			}

			return list;
		}

		/// <summary>
		/// 與 vanilla ScenPart_PlayerPawnsArriveMethod.DoDropPods 等價的保底路徑。
		/// Fallback identical to vanilla's drop-pod arrival, used whenever the gravship path is unusable.
		/// </summary>
		private void DoDropPods(Map map, List<Thing> startingItems)
		{
			List<List<Thing>> list = new List<List<Thing>>();
			foreach (Pawn pawn in Find.GameInitData.startingAndOptionalPawns)
			{
				if (pawn == null) continue;
				list.Add(new List<Thing> { pawn });
			}
			if (list.Count == 0)
			{
				// 沒有小人可投放時，至少別把物品弄丟。
				// No pawns to drop with; still make sure the items are not silently lost.
				if (!startingItems.NullOrEmpty()) list.Add(new List<Thing>(startingItems));
			}
			else
			{
				int num = 0;
				foreach (Thing startingItem in startingItems)
				{
					if (startingItem == null) continue;
					if (startingItem.def.CanHaveFaction) startingItem.SetFactionDirect(Faction.OfPlayer);
					list[num].Add(startingItem);
					num++;
					if (num >= list.Count) num = 0;
				}
			}
			if (list.Count == 0) return;

			DropPodUtility.DropThingGroupsNear(MapGenerator.PlayerStartSpot, map, list, 110,
				Find.GameInitData.QuickStarted, leaveSlag: true, canRoofPunch: true, forbid: true, allowFogged: false);
		}

		private void DoGravship(Map map, List<Thing> startingItems)
		{
			Sketch sketch = gravshipStructure.GetSketch();
			if (sketch == null || !sketch.OccupiedRect.Cells.Any())
			{
				throw new InvalidOperationException($"結構 {gravshipStructure.defName} 產生的 sketch 為空。Structure {gravshipStructure.defName} produced an empty sketch.");
			}

			Rot4 rot = Rot4.Random;
			if (rot != Rot4.North) sketch.Rotate(rot);

			HashSet<IntVec3> footprint = sketch.OccupiedRect.Cells.Select((IntVec3 c) => c - sketch.OccupiedCenter).ToHashSet();
			List<CellRect> usedRects = MapGenerator.GetOrGenerateVar<List<CellRect>>("UsedRects") ?? new List<CellRect>();
			map.regionAndRoomUpdater.Enabled = true;

			IntVec3 playerStartSpot = MapGenerator.PlayerStartSpot;
			if (!MapGenerator.PlayerStartSpotValid)
			{
				GenStep_ReserveGravshipArea.SetStartSpot(map, footprint, usedRects);
				playerStartSpot = MapGenerator.PlayerStartSpot;
			}
			if (!playerStartSpot.IsValid || !playerStartSpot.InBounds(map))
			{
				playerStartSpot = map.Center;
			}

			GravshipPlacementUtility.ClearAreaForGravship(map, playerStartSpot, footprint);

			// FFF_StructureUtility.Generate 內部以 sketch.OccupiedRect.CenterCell 對位，
			// 這裡用同一套算法還原實際占用範圍，避免兩邊錯位。
			// Generate() positions the structure by its sketch's OccupiedRect.CenterCell;
			// mirror that math so the rect we use below matches what actually got spawned.
			CellRect cellRect = sketch.OccupiedRect.MovedBy(playerStartSpot - sketch.OccupiedCenter).ClipInsideMap(map);

			FFF_StructureUtility.Generate(gravshipStructure, playerStartSpot, map, Faction.OfPlayer, rot, reconnectPower: false);

			usedRects.Add(cellRect);

			List<Thing> spawnedThings = CollectSpawnedBuildings(map, cellRect);

			PlaceStartingPawns(map, cellRect, playerStartSpot);
			PlaceStartingItems(map, spawnedThings, startingItems, playerStartSpot);
			PostProcessSpawnedThings(spawnedThings);
			MarkHomeArea(map, cellRect);
		}

		/// <summary>
		/// 收集結構範圍內剛生成的建築。Generate() 不回傳生成物，只能就地掃描。
		/// Collect the buildings the structure just spawned; Generate() does not hand back a list.
		/// </summary>
		private static List<Thing> CollectSpawnedBuildings(Map map, CellRect rect)
		{
			List<Thing> list = new List<Thing>();
			HashSet<Thing> seen = new HashSet<Thing>();
			foreach (IntVec3 cell in rect)
			{
				if (!cell.InBounds(map)) continue;
				List<Thing> thingList = cell.GetThingList(map);
				for (int i = 0; i < thingList.Count; i++)
				{
					Thing t = thingList[i];
					if (t == null || t is Pawn) continue;
					if (t.def?.category != ThingCategory.Building) continue;
					if (seen.Add(t)) list.Add(t);
				}
			}
			return list;
		}

		private static void PlaceStartingPawns(Map map, CellRect cellRect, IntVec3 fallbackSpot)
		{
			List<Pawn> pawns = Find.GameInitData?.startingAndOptionalPawns;
			if (pawns.NullOrEmpty()) return;

			foreach (Pawn pawn in pawns)
			{
				if (pawn == null || pawn.Spawned) continue;

				IntVec3 spot;
				if (!cellRect.TryRandomElement((IntVec3 c) => c.InBounds(map) && c.Standable(map) && (c.GetTerrain(map)?.IsSubstructure ?? false), out spot)
					&& !cellRect.TryRandomElement((IntVec3 c) => c.InBounds(map) && c.Standable(map), out spot))
				{
					// 結構裡找不到落腳點時退回開局點，絕不能讓小人生不出來。
					// Never leave a starting pawn unspawned; fall back to the player start spot.
					Log.Warning("[FFF] 結構內找不到 " + pawn.LabelShortCap + " 的合法生成位置，改放到開局點。 " +
						"No valid spawn cell inside the structure for " + pawn.LabelShortCap + "; using the player start spot.");
					spot = fallbackSpot;
				}

				if (!GenPlace.TryPlaceThing(pawn, spot, map, ThingPlaceMode.Near))
				{
					GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(spot, map, 12), map);
				}
			}
		}

		private static void PlaceStartingItems(Map map, List<Thing> spawnedThings, List<Thing> startingItems, IntVec3 fallbackSpot)
		{
			if (startingItems.NullOrEmpty()) return;

			List<Thing> shelves = spawnedThings
				.Where((Thing t) => t.def == ThingDefOf.Shelf || t.def == ThingDefOf.ShelfSmall)
				.ToList();

			foreach (Thing startingItem in startingItems)
			{
				if (startingItem == null || startingItem.Destroyed) continue;
				if (startingItem.def.CanHaveFaction) startingItem.SetFactionDirect(Faction.OfPlayer);

				int remaining = startingItem.stackCount;
				int guard = 99;
				while (remaining > 0 && guard-- > 0 && shelves.Count > 0)
				{
					if (!shelves.TryRandomElement(out Thing shelf) || shelf == null || !shelf.Spawned) break;

					IntVec3 cell = shelf.OccupiedRect().RandomCell;
					int take = Math.Min(Math.Max(startingItem.def.stackLimit, 1), remaining);
					Thing piece = (take >= startingItem.stackCount) ? startingItem : startingItem.SplitOff(take);
					remaining -= piece.stackCount;
					if (!GenPlace.TryPlaceThing(piece, cell, map, ThingPlaceMode.Near))
					{
						DropAt(piece, map, fallbackSpot);
					}
					if (piece == startingItem) break;
				}

				// 沒有貨架、或塞不下的剩餘部分，一律落地，不能憑空消失。
				// Anything that could not go on a shelf is dropped at the start spot instead of vanishing.
				if (!startingItem.Spawned && !startingItem.Destroyed && startingItem.stackCount > 0)
				{
					DropAt(startingItem, map, fallbackSpot);
				}
			}
		}

		private static void DropAt(Thing thing, Map map, IntVec3 spot)
		{
			if (thing == null || thing.Spawned || thing.Destroyed) return;
			if (!GenPlace.TryPlaceThing(thing, spot, map, ThingPlaceMode.Near))
			{
				GenPlace.TryPlaceThing(thing, CellFinder.RandomClosewalkCellNear(spot, map, 12), map, ThingPlaceMode.Near);
			}
		}

		private static void PostProcessSpawnedThings(List<Thing> spawnedThings)
		{
			foreach (Thing item in spawnedThings)
			{
				if (item == null || !item.Spawned) continue;

				if (item.def == ThingDefOf.Door && MapGenerator.rootsToUnfog != null)
				{
					MapGenerator.rootsToUnfog.AddRange(GenAdj.CellsAdjacentCardinal(item));
				}
				if (item.TryGetComp(out CompRefuelable comp) && comp.Props != null)
				{
					comp.Refuel(comp.Props.fuelCapacity);
				}
				if (item is Building_GravEngine building_GravEngine)
				{
					building_GravEngine.silentlyActivate = true;
				}
			}
		}

		private static void MarkHomeArea(Map map, CellRect cellRect)
		{
			if (map.areaManager?.Home == null) return;
			foreach (IntVec3 cell in cellRect)
			{
				if (!cell.InBounds(map)) continue;
				if (cell.GetTerrain(map) == TerrainDefOf.Substructure)
				{
					map.areaManager.Home[cell] = true;
				}
			}
		}

		public override void PostMapGenerate(Map map)
		{
			if (Find.GameInitData != null && usedDropPodFallback)
			{
				PawnUtility.GiveAllStartingPlayerPawnsThought(ThoughtDefOf.CrashedTogether);
			}
		}

		/// <summary>
		/// ScenarioLister 會在場景選單開啟前對每個 Scenario 取雜湊，
		/// 此時 gravshipStructure 可能還是 null（XML 未指定／舊存檔），不能直接 .GetHashCode()。
		///
		/// ScenarioLister hashes every scenario before the select-scenario page opens, while
		/// gravshipStructure may still be null (unset in XML, or an older saved scenario).
		/// Dereferencing it there threw an NRE inside OnGUI.
		/// </summary>
		public override int GetHashCode()
		{
			return base.GetHashCode() ^ (gravshipStructure?.GetHashCode() ?? 0);
		}
	}
}
