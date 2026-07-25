// 当白昼倾坠之时
using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Fortified.Structures
{
    // 场景部分开局添加结构逻辑
    public class ScenPart_AddStartingStructure : ScenPart
    {
        public string structureLabel;
        public bool nearMapCenter = true;
        public List<string> chooseFrom = new List<string>();
        public bool spawnPartOfEnemyFaction = false;
        public bool spawnTheStartingPawn = false;
        public bool randomRotation = false;

        // 地图生成后处理结构生成
        public override void PostMapGenerate(Map map)
        {
            if (map == null || !map.IsStartingMap || map.IsPocketMap) return;
            if (chooseFrom.NullOrEmpty()) return;

            var layout = DefDatabase<StructureLayoutDef>.GetNamedSilentFail(chooseFrom.RandomElement());
            if (layout == null) return;
                
            IntVec3 pos = nearMapCenter ? map.Center : CellFinder.RandomCell(map);
            Rot4 rot = randomRotation ? Rot4.Random : Rot4.North;
            
            Faction fac = spawnPartOfEnemyFaction ? Find.FactionManager.RandomEnemyFaction() : Faction.OfPlayer;
            FFF_StructureUtility.Generate(layout, pos, map, fac, rot, reconnectPower: false);

            NotifyAndMovePawns(map, pos);
        }

        private void NotifyAndMovePawns(Map map, IntVec3 pos)
        {
            if (!structureLabel.NullOrEmpty())
            {
                Messages.Message("FFF_StructureGenerated".Translate(structureLabel), MessageTypeDefOf.PositiveEvent);
            }

            if (!spawnTheStartingPawn) return;
            
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                p.Position = pos; // Simplified move
                p.Notify_Teleported();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref structureLabel, "structureLabel");
            Scribe_Values.Look(ref nearMapCenter, "nearMapCenter", true);
            Scribe_Collections.Look(ref chooseFrom, "chooseFrom", LookMode.Value);
            Scribe_Values.Look(ref spawnPartOfEnemyFaction, "spawnPartOfEnemyFaction");
            Scribe_Values.Look(ref spawnTheStartingPawn, "spawnTheStartingPawn");
            Scribe_Values.Look(ref randomRotation, "randomRotation", false);
        }
    }

    // 场景部分强制派系好感度逻辑
    public class ScenPart_ForcedFactionGoodwill : ScenPart
    {
        public FactionDef factionDef;
        public IntRange startingGoodwillRange = IntRange.Zero;
        public IntRange naturalGoodwillRange = IntRange.Zero;
        public bool alwaysHostile;
        public bool affectHiddenFactions;
        public bool affectStartingGoodwill;
        public bool affectNaturalGoodwill;

        // 世界生成后应用初始好感度
        public override void PostWorldGenerate()
        {
            if (!affectStartingGoodwill && !alwaysHostile) return;
            foreach (var f in Find.FactionManager.AllFactions)
            {
                if (Affects(f))
                {
                    ApplyInitialGoodwill(f);
                }
            }
        }

        private void ApplyInitialGoodwill(Faction f)
        {
            int target = alwaysHostile ? -100 : startingGoodwillRange.RandomInRange;
            f.TryAffectGoodwillWith(Faction.OfPlayer, target - f.PlayerGoodwill, false, false);
        }

        // 检查派系是否符合规则
        public bool Affects(Faction f)
        {
            return Affects(f.def);
        }

        public bool Affects(FactionDef def)
        {
            if (def.isPlayer) return false;
            if (factionDef != null && factionDef != def) return false;
            if (!affectHiddenFactions && def.hidden) return false;
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref factionDef, "factionDef");
            Scribe_Values.Look(ref alwaysHostile, "alwaysHostile");
            Scribe_Values.Look(ref affectHiddenFactions, "affectHiddenFactions");
            Scribe_Values.Look(ref affectStartingGoodwill, "affectStartingGoodwill");
            Scribe_Values.Look(ref startingGoodwillRange, "startingGoodwillRange");
            Scribe_Values.Look(ref affectNaturalGoodwill, "affectNaturalGoodwill");
            Scribe_Values.Look(ref naturalGoodwillRange, "naturalGoodwillRange");
        }
    }

    // 世界生成时生成的扩展
    public class SpawnAtWorldGen : DefModExtension
    {
        public int spawnCount = 1;
        public FactionDef spawnPartOfFaction = null;
        public List<SitePartDef> parts = new List<SitePartDef>();
        public List<BiomeDef> allowedBiomes = new List<BiomeDef>();
        public List<BiomeDef> disallowedBiomes = new List<BiomeDef>();
    }
}
