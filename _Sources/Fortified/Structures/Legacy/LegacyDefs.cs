// 当白昼倾坠之时
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Fortified.Structures
{
    // 兼容旧版 KCSG 布局定义
    public class StructureLayoutDef : Def, IFFF_Structure, IFFF_PawnProvider, IFFF_TaskProvider
    {
        public List<List<string>> layouts = new List<List<string>>();
        public List<string> terrainGrid = new List<string>();
        public List<string> foundationGrid = new List<string>(); // 新增
        public List<string> underGrid = new List<string>();      // 新增
        public List<string> tempGrid = new List<string>();       // 新增
        public List<string> roofGrid = new List<string>();
        public List<string> terrainColorGrid = new List<string>();
        public int maxSize; // 兼容旧版最大尺寸字段

        public bool spawnConduits = true;
        public bool forceGenerateRoof = false;
        public bool needRoofClearance = false;
        public bool randomRotation = false;
        public List<string> tags = new List<string>();
        public List<string> modRequirements = new List<string>();
        public int frontDist = 0;

        public int FrontDist => frontDist;
        public IntVec2 Size => cachedSketch != null ? cachedSketch.OccupiedRect.Size : new IntVec2(maxSize, maxSize);

        [Unsaved]
        private Sketch cachedSketch;
        [Unsaved]
        private List<FFF_PawnGenRequest> cachedPawns;
        [Unsaved]
        private List<IFFF_GenerationTask> cachedTasks;

        // 获取缓存或生成的草图
        public Sketch GetSketch()
        {
            if (cachedSketch == null)
            {
                cachedSketch = new Sketch();
                cachedPawns = new List<FFF_PawnGenRequest>();
                cachedTasks = new List<IFFF_GenerationTask>();
                LegacyParser.ParseToSketch(this, cachedSketch, cachedPawns, cachedTasks);
            }

            // 一定要回傳副本，與 FFF_StructureDef.GetSketch 一致。
            // 呼叫端（FFF_StructureUtility.Generate / FootprintAt）會直接對回傳值呼叫 Rotate()，
            // 交出快取本體等於永久轉壞這個 def：randomRotation 開啟時角度會跨地圖累加，
            // 而且單次生成裡 FootprintAt → Generate 會轉兩次，預測的足跡與實際落點長寬互換，
            // 後續的界內判定與衛星間距全部算在錯的矩形上。
            //
            // Must hand back a copy, as FFF_StructureDef.GetSketch already does. Callers rotate
            // the returned sketch in place, so returning the cache permanently corrupts the def:
            // with randomRotation the angle accumulates across maps, and within a single run
            // FootprintAt → Generate rotates twice, leaving the predicted footprint transposed
            // relative to the real one — every bounds and spacing check then works off a wrong rect.
            return cachedSketch.DeepCopy();
        }

        // 获取布局关联的 Pawn 列表
        public List<FFF_PawnGenRequest> GetPawns()
        {
            if (cachedPawns == null) GetSketch();
            return cachedPawns;
        }

        public List<IFFF_GenerationTask> GetGenerationTasks()
        {
            if (cachedTasks == null) GetSketch();
            return cachedTasks;
        }

        public List<FFF_PawnGenRequest> GetPawns(Rot4 rot, IntVec3 offset)
        {
            return GetPawns().ConvertAll(p => new FFF_PawnGenRequest 
            { 
                Kind = p.Kind, 
                Faction = p.Faction, 
                Position = p.Position.RotatedBy(rot) + offset,
                DefendSpawnPoint = p.DefendSpawnPoint
            });
        }

        public List<IFFF_GenerationTask> GetTasks(Rot4 rot, IntVec3 offset)
        {
            return GetGenerationTasks().ConvertAll(t => t.Transformed(rot, offset));
        }
    }

    // 结构符号定义
    public class SymbolDef : Def
    {
        public ThingDef thing;
        public TerrainDef terrain; // 新增支持地形符号
        public ThingDef stuff;
        public Rot4 rotation = Rot4.North;
        public string color;

        public string pawnKindDef;
        public string faction;
        public bool defendSpawnPoint;

        public string replacementDef; // 兼容旧版替换定义
        public int maxStackSize; // 兼容旧版堆叠限制
        public ThingSetMakerDef thingSetMakerDef; // 兼容旧版生成器定义
        public float? fuelPercent; // 兼容旧版燃料比例
        public float? powerPercent; // 兼容旧版电力比例
        public float crateStackMultiplier = 1f; // 兼容旧版木箱堆叠乘数
        public float chanceToContainPawn = 1f; // 兼容旧版容器包含小人概率
        public float plantGrowth = 1f; // 兼容旧版植物生长度

        public SymbolDef ShallowCopy()
        {
            return (SymbolDef)MemberwiseClone();
        }
        
        // 将符号内容添加到草图
        public void AddToSketch(Sketch sketch, IntVec3 pos, List<FFF_PawnGenRequest> outPawns = null, List<IFFF_GenerationTask> outTasks = null)
        {
            if (terrain != null)
            {
                sketch.AddTerrain(terrain, pos);
            }

            if (!pawnKindDef.NullOrEmpty())
            {
                AddPawnToRequest(pos, outPawns);
                return;
            }
            AddThingToSketch(sketch, pos, outTasks);
        }

        private void AddPawnToRequest(IntVec3 pos, List<FFF_PawnGenRequest> outPawns)
        {
            if (outPawns == null) return;
            var pk = DefDatabase<PawnKindDef>.GetNamedSilentFail(pawnKindDef);
            if (pk == null) return;
            var fac = !faction.NullOrEmpty() ? DefDatabase<FactionDef>.GetNamedSilentFail(faction) : null;
            outPawns.Add(new FFF_PawnGenRequest { Kind = pk, Faction = fac, Position = pos, DefendSpawnPoint = defendSpawnPoint });
        }

        private void AddThingToSketch(Sketch sketch, IntVec3 pos, List<IFFF_GenerationTask> outTasks)
        {
            if (thing == null) return;
            sketch.AddThing(thing, pos, rotation, stuff);

            if (outTasks != null) AddDecorationTasks(pos, outTasks);
        }

        private void AddDecorationTasks(IntVec3 pos, List<IFFF_GenerationTask> outTasks)
        {
            if (thingSetMakerDef != null)
                outTasks.Add(new Task_FillContainer { pos = pos, makerDef = thingSetMakerDef, stackMultiplier = crateStackMultiplier });

            if (thing.building != null && thing.building.IsMortar)
                outTasks.Add(new Task_ManMortar { pos = pos, factionDef = faction != null ? DefDatabase<FactionDef>.GetNamedSilentFail(faction) : null });

            if (ShouldAddStateTask())
            {
                var task = new Task_SetThingState { pos = pos, forbidden = true };
                if (color != null) task.color = DefDatabase<ColorDef>.GetNamedSilentFail(color);
                if (thing.category == ThingCategory.Plant) task.plantGrowth = plantGrowth;
                outTasks.Add(task);
            }
        }

        private bool ShouldAddStateTask()
        {
            return thing.category == ThingCategory.Item || color != null || thing.category == ThingCategory.Plant;
        }
    }
}
