// 当白昼倾坠之时
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Fortified.Structures
{
    // 在世界生成时放置特定地点
    public class WorldGenStep_FFFSpawnWorldObjects : WorldGenStep
    {
        public override int SeedPart => 1616161616;

        public override void GenerateFresh(string seed, PlanetLayer layer)
        {
            var worldobjects = DefDatabase<WorldObjectDef>.AllDefsListForReading
                .Where(wo => wo.HasModExtension<SpawnAtWorldGen>());

            foreach (var worldObject in worldobjects)
            {
                SpawnAtWorldGen ext = worldObject.GetModExtension<SpawnAtWorldGen>();
                SpawnObjects(worldObject, ext);
            }
        }

        /// <summary>
        /// 依 SpawnAtWorldGen 放下 count 座（預設 spawnCount）站點，回傳實際放了幾座。世界生成與舊存檔補生成共用。
        /// Places count sites (spawnCount by default) per SpawnAtWorldGen and returns how many landed. Shared by world
        /// generation and the old-save backfill.
        /// </summary>
        public static int SpawnObjects(WorldObjectDef def, SpawnAtWorldGen ext, int count = -1)
        {
            int placed = 0;
            for (int i = 0; i < (count < 0 ? ext.spawnCount : count); i++)
            {
                Site wo = (Site)WorldObjectMaker.MakeWorldObject(def);
                
                // 在设置Tile前先设置Faction，确保后续生成的Thing能正确继承Faction
                Faction faction = null;
                if (ext.spawnPartOfFaction != null)
                {
                    faction = Find.FactionManager.FirstFactionOfDef(ext.spawnPartOfFaction);
                    wo.SetFaction(faction);
                }
                
                wo.Tile = RandomTileFor(ext);
                if (wo.Tile <= 0) continue;

                AddPartsToSite(wo, ext, faction);
                Find.WorldObjects.Add(wo);
                placed++;
            }
            return placed;
        }

        private static void AddPartsToSite(Site wo, SpawnAtWorldGen ext, Faction faction)
        {
            SitePartParams parms = new SitePartParams { points = 500f };
            foreach (var part in ext.parts)
            {
                SitePart sitePart = new SitePart(wo, part, parms);
                wo.AddPart(sitePart);
            }
        }

        private static int RandomTileFor(SpawnAtWorldGen ext)
        {
            for (int i = 0; i < 500; i++)
            {
                int tileID = Rand.Range(0, Find.WorldGrid.TilesCount);
                if (IsTileValid(tileID, ext)) return tileID;
            }
            return -1;
        }

        private static bool IsTileValid(int tileID, SpawnAtWorldGen ext)
        {
            Tile tile = Find.WorldGrid[tileID];
            if (!tile.PrimaryBiome.canBuildBase || tile.hilliness == Hilliness.Impassable) return false;
            if (Find.WorldObjects.AnyWorldObjectAt(tileID) || Find.WorldObjects.AnySettlementBaseAtOrAdjacent(tileID)) return false;
            // 地標的突變會跟站點結構搶同一片地；原版地標在 order 650 才生成且會避開已有站點，這裡防的是更早放下的地標。
            // Landmark mutators fight site structures for the same ground. Vanilla places landmarks later (order 650) and
            // skips existing sites; this guards against landmarks placed earlier by other world gen steps.
            if (Fortified.FFF_SiteTileUtility.IsLandmarkTile(tileID)) return false;
            if (ext.allowedBiomes.Any() && !ext.allowedBiomes.Contains(tile.PrimaryBiome)) return false;
            if (ext.disallowedBiomes.Contains(tile.PrimaryBiome)) return false;
            return true;
        }
    }
}
