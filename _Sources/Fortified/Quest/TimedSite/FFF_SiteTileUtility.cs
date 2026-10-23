using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 站點選格：避開地標地塊。原版 TileFinder.TryFindNewSiteTile 預設 selectLandmarkChance = 0.5，
    /// 一半的站點會被刻意丟到地標上；地標的突變（古代廢墟、古代結構……）在 order 500/700 生成，
    /// 跟我們在 order 400 左右的營地搶同一片地，兩邊都殘缺。DMS2 的石碑站點用 LandmarkMode.Forbidden 解過，
    /// 這裡把同一套做成共用工具：先找非地標格，真的找不到才退回原版行為。
    ///
    /// Site tile search that keeps off landmark tiles. Vanilla's TryFindNewSiteTile defaults to
    /// selectLandmarkChance = 0.5, so half of all sites are put on a landmark on purpose; the landmark's
    /// mutators (ancient ruins, ancient structures…) generate at order 500/700 and fight our compound (order ~400)
    /// for the same ground, leaving both broken. DMS2's stele sites solved this with LandmarkMode.Forbidden;
    /// this is the shared version: non-landmark tiles first, vanilla behaviour only as a last resort.
    /// </summary>
    public static class FFF_SiteTileUtility
    {
        /// <summary>該地塊是否帶地標。Whether the tile carries a landmark.</summary>
        public static bool IsLandmarkTile(PlanetTile tile)
        {
            if (!ModsConfig.OdysseyActive || !tile.Valid) return false;
            return Find.World?.landmarks?[tile] != null;
        }

        /// <summary>
        /// 去掉地標地塊；全是地標時原樣回傳，讓呼叫端仍有地方可去（例如移動中的世界物件挑下一格）。
        /// Drops landmark tiles; returns the list unchanged when every tile is one, so callers (e.g. a moving world
        /// object picking its next tile) still have somewhere to go.
        /// </summary>
        public static List<PlanetTile> WithoutLandmarks(this List<PlanetTile> tiles)
        {
            if (tiles.NullOrEmpty()) return tiles;
            List<PlanetTile> clear = tiles.FindAll(t => !IsLandmarkTile(t));
            return clear.Count > 0 ? clear : tiles;
        }

        /// <summary>
        /// 同 TileFinder.TryFindNewSiteTile，但不選地標地塊；找不到時退回原版（可能落在地標上）。
        /// Same as TileFinder.TryFindNewSiteTile without landmark tiles; falls back to vanilla (landmarks allowed) when nothing fits.
        /// </summary>
        public static bool TryFindNewSiteTile(out PlanetTile tile, int minDist = 7, int maxDist = 27, bool allowCaravans = false,
            bool exitOnFirstTileFound = false, Predicate<PlanetTile> validator = null)
        {
            Predicate<PlanetTile> noLandmark = t => !IsLandmarkTile(t) && (validator == null || validator(t));
            if (TileFinder.TryFindNewSiteTile(out tile, minDist, maxDist, allowCaravans, selectLandmarkChance: 0f,
                    exitOnFirstTileFound: exitOnFirstTileFound, validator: noLandmark))
            {
                return true;
            }
            return TileFinder.TryFindNewSiteTile(out tile, minDist, maxDist, allowCaravans, selectLandmarkChance: 0f,
                exitOnFirstTileFound: exitOnFirstTileFound, validator: validator);
        }
    }
}
