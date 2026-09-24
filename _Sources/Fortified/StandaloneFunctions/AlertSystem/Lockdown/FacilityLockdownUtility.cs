using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Fortified
{
    public static class FacilityLockdownUtility
    {
        /// <summary>找通往這張口袋地圖的地表入口。Find the surface portal whose pocket map this is.</summary>
        public static MapPortal FindSurfacePortal(Map pocketMap)
        {
            if (pocketMap == null) return null;

            // 先從地下出口反查：原版 PocketMapExit.entrance 就是地表那一端。
            // Try the exit first: vanilla's PocketMapExit.entrance is the surface end.
            foreach (Building b in pocketMap.listerBuildings.allBuildingsNonColonist)
            {
                if (b is PocketMapExit exit && exit.entrance != null) return exit.entrance;
            }
            foreach (Building b in pocketMap.listerBuildings.allBuildingsColonist)
            {
                if (b is PocketMapExit exit && exit.entrance != null) return exit.entrance;
            }

            Map surface = (pocketMap.Parent as PocketMapParent)?.sourceMap;
            if (surface == null) return null;
            foreach (Thing t in surface.listerThings.AllThings)
            {
                if (t is MapPortal portal && !(t is PocketMapExit) && portal.PocketMap == pocketMap) return portal;
            }
            return null;
        }

        /// <summary>
        /// 困在裡面的玩家 pawn 判定失蹤，並移除口袋地圖。pawn 保留在世界 pawn 中（陣營清空），日後可以做救援。
        /// Trapped player pawns go missing and the pocket map is removed. The pawns stay as world pawns
        /// (factionless) so a rescue could be written later.
        /// </summary>
        public static void DeclareLost(Map pocketMap, MapComponent_FacilityLockdown lockdown)
        {
            if (pocketMap == null) return;
            List<Pawn> trapped = pocketMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).ToList();
            MapPortal surface = FindSurfacePortal(pocketMap);
            lockdown?.Notify_Lost();

            foreach (Pawn pawn in trapped)
            {
                PawnDiedOrDownedThoughtsUtility.TryGiveThoughts(pawn, null, PawnDiedOrDownedThoughtsKind.Lost);
                pawn.DeSpawn(DestroyMode.Vanish);
                pawn.SetFaction(null);
                if (!pawn.IsWorldPawn())
                {
                    Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
                }
            }

            if (trapped.Count > 0)
            {
                Find.LetterStack.ReceiveLetter(
                    "FFF_Lockdown_LostLabel".Translate(),
                    "FFF_Lockdown_LostText".Translate(trapped.Select(p => p.LabelShort).ToLineList("  - ")),
                    LetterDefOf.Death, surface);
            }

            // 先讓地表端記住封死，再移除口袋地圖。Mark the surface end sealed before the pocket map goes.
            surface?.TryGetComp<CompFacilityLockdownGate>()?.Notify_PermanentlySealed();
            PocketMapUtility.DestroyPocketMap(pocketMap);
        }
    }
}
