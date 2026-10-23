using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Fortified.Structures
{
    /// <summary>
    /// 世界生成站點的舊存檔修補：
    /// 1. 補生成：1.6 起 FFF_SpawnWorldObjects 沒有列在地表層的 worldGenSteps，從來沒有執行，SpawnAtWorldGen 的站點一座都沒放。
    ///    每個存檔只做一次：某種站點完全不存在時，照設定補放。
    /// 2. 補派系：曾在派系生成前執行過的站點沒有派系（巡邏不出動、結構沒有主人），補上 spawnPartOfFaction。
    ///
    /// Old-save repair for world-gen sites:
    /// 1. Backfill: since 1.6 FFF_SpawnWorldObjects wasn't in the surface layer's worldGenSteps and never ran, so no
    ///    SpawnAtWorldGen site was ever placed. Once per save, a site type with none in the world is spawned as configured.
    /// 2. Faction: sites placed before factions existed have no faction (no patrols, ownerless structures); give them
    ///    their spawnPartOfFaction.
    /// </summary>
    public class GameComponent_WorldGenSiteFactionRepair : GameComponent
    {
        private bool backfilled;

        public GameComponent_WorldGenSiteFactionRepair(Game game) { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            if (Find.WorldObjects == null) return;

            if (!backfilled)
            {
                backfilled = true;
                foreach (WorldObjectDef def in DefDatabase<WorldObjectDef>.AllDefsListForReading.Where(d => d.HasModExtension<SpawnAtWorldGen>()))
                {
                    if (Find.WorldObjects.AllWorldObjects.Any(o => o.def == def)) continue;
                    int placed = WorldGenStep_FFFSpawnWorldObjects.SpawnObjects(def, def.GetModExtension<SpawnAtWorldGen>());
                    if (placed > 0) Log.Message($"[FortifiedFramework] Backfilled {placed} world-gen {def.defName} site(s) missing from this save.");
                }
            }

            int repaired = 0;
            foreach (Site site in Find.WorldObjects.Sites)
            {
                if (site.Faction != null) continue;
                FactionDef def = site.def.GetModExtension<SpawnAtWorldGen>()?.spawnPartOfFaction;
                Faction faction = def != null ? Find.FactionManager.FirstFactionOfDef(def) : null;
                if (faction == null) continue;
                site.SetFaction(faction);
                repaired++;
            }
            if (repaired > 0) Log.Message($"[FortifiedFramework] Gave {repaired} world-gen site(s) their missing faction.");
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref backfilled, "backfilled");
        }
    }
}
