// 当白昼倾坠之时
using System.Linq;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using RimWorld.Planet;

namespace Fortified.Structures
{
    public class GenStep_FFFStructureGen : GenStep
    {
        public GenStep_FFFStructureGen() { }

        public override int SeedPart => 394857125;

        // XML 字段兼容 KCSG
        public List<StructureLayoutDef> structureLayoutDefs;
        public List<FFF_CompoundStructureDef> compoundStructureDefs; // 新增
        public string useTag; // 如果设置，则从具有此标签的定义中随机选取
        public bool fullClear = true;
        public bool preventBridgeable = false;
        public List<string> filthTypes; // 污垢散射
        public List<string> symbolResolvers; // BaseGen 符号解析器
        public bool scaleWithQuest; // 维持 XML 兼容性
        public bool randomRotation = false;
        public IntRange randomOffset = new IntRange(0, 5);

        // 本次生成的主结构落点，供子类（例如卫星散布）使用。
        // Where the main structure landed this run, for subclasses such as satellite scatter.
        protected IFFF_Structure lastStructureDef;
        protected IntVec3 lastStructureCenter;
        protected Rot4 lastStructureRot = Rot4.North;
        protected CellRect lastStructureRect = default;

        /// <summary>
        /// lastStructureRect 是否有效。用旗標而不是檢查 rect 是否為空 ——
        /// default(CellRect) 的四個邊界都是 0，看起來像一個位在原點的 1x1 合法範圍。
        /// Whether lastStructureRect holds a real footprint. A flag rather than an
        /// emptiness probe: default(CellRect) has all four edges at 0, which reads as a
        /// legitimate 1x1 rect at the origin.
        /// </summary>
        protected bool hasLastStructure;

        public override void Generate(Map map, GenStepParams parms)
        {
            IntVec3 center = CalculateCenter(map);
            Faction faction = map.ParentFaction ?? parms.sitePart?.site?.Faction;

            // 优先尝试复合结构
            FFF_CompoundStructureDef compoundDef = ResolveCompoundDef();
            if (compoundDef != null)
            {
                CompoundStructureUtility.Generate(compoundDef, center, map, faction);
                ScatterSatellites(map, center, faction, null);
                return;
            }

            // 回退到单一结构
            IFFF_Structure def = ResolveStructureDef();
            if (def == null) return;

            Rot4 rot = randomRotation ? Rot4.Random : Rot4.North;

            lastStructureDef = def;
            lastStructureCenter = center;
            lastStructureRot = rot;
            lastStructureRect = FFF_StructureUtility.FootprintAt(def, center, rot);
            hasLastStructure = true;

            // reconnectPower: false —— 生成期不強制刷新電網，見 Generate 的參數說明。
            FFF_StructureUtility.Generate(def, center, map, faction, rot, reconnectPower: false);
            HandlePostScatter(def, center, map, rot);
            ScatterSatellites(map, center, faction, RectOf(center, def.Size, rot));
        }

        private FFF_CompoundStructureDef ResolveCompoundDef()
        {
            if (!compoundStructureDefs.NullOrEmpty())
                return compoundStructureDefs.RandomElementWithFallback();

            if (!useTag.NullOrEmpty())
            {
                var matches = DefDatabase<FFF_CompoundStructureDef>.AllDefs
                    .Where(x => x.label != null && x.label.Contains(useTag));
                return matches.RandomElementWithFallback();
            }
            return null;
        }

        private void HandlePostScatter(IFFF_Structure def, IntVec3 center, Map map, Rot4 rot)
        {
            CellRect rect = FFF_StructureUtility.FootprintAt(def, center, rot);

            // 1. 处理污垢散射
            if (!filthTypes.NullOrEmpty())
            {
                List<ThingDef> filthDefs = filthTypes.Select(x => DefDatabase<ThingDef>.GetNamedSilentFail(x)).Where(x => x != null).ToList();
                if (filthDefs.Count > 0)
                {
                    new Task_ScatterFilth { rect = rect, filthTypes = filthDefs, chance = 0.25f }.Execute(map, IntVec3.Zero);
                }
            }

            // 2. 处理 SymbolResolvers (KCSG 兼容)
            if (!symbolResolvers.NullOrEmpty())
            {
                var rp = new RimWorld.BaseGen.ResolveParams { rect = rect };
                foreach (string resolver in symbolResolvers)
                {
                    RimWorld.BaseGen.BaseGen.symbolStack.Push(resolver, rp, null);
                }
                RimWorld.BaseGen.BaseGen.Generate();
            }
        }

        protected IFFF_Structure ResolveStructureDef()
        {
            if (!useTag.NullOrEmpty())
                return RandomStructureWithTag(useTag);

            if (!structureLayoutDefs.NullOrEmpty())
                return structureLayoutDefs.RandomElementWithFallback();

            return null;
        }

        /// <summary>
        /// 從所有結構庫（舊 StructureLayoutDef、FFF_StructureDef、FFF_SettlementDef）
        /// 中按標籤隨機取一個。每次呼叫都重新擲骰，不快取。
        ///
        /// Random structure carrying <paramref name="tag"/>, across all three structure
        /// databases. Rolled fresh on every call — deliberately not cached.
        /// </summary>
        public static IFFF_Structure RandomStructureWithTag(string tag)
        {
            if (tag.NullOrEmpty()) return null;

            var matches = DefDatabase<StructureLayoutDef>.AllDefs.Where(x => x.tags != null && x.tags.Contains(tag)).Cast<IFFF_Structure>()
                .Concat(DefDatabase<FFF_StructureDef>.AllDefs.Where(x => x.tags != null && x.tags.Contains(tag)).Cast<IFFF_Structure>())
                .Concat(DefDatabase<FFF_SettlementDef>.AllDefs.Where(x => x.tags != null && x.tags.Contains(tag)).Cast<IFFF_Structure>());
            return matches.RandomElementWithFallback();
        }

        private IntVec3 CalculateCenter(Map map)
        {
            IntVec3 center = map.Center;
            if (randomOffset.max > 0)
                center += new IntVec3(randomOffset.RandomInRange, 0, randomOffset.RandomInRange).RotatedBy(Rot4.Random);
            return center;
        }

        private Faction ResolveFaction(IFFF_Structure def, Map map, GenStepParams parms)
        {
            Faction faction = parms.sitePart?.site?.Faction;
            var pawns = def.GetPawns(Rot4.North, IntVec3.Zero);
            if (faction == null && pawns?.Count > 0)
            {
                var firstReq = pawns[0];
                if (firstReq.Faction != null) faction = Find.FactionManager.FirstFactionOfDef(firstReq.Faction);
            }
            return faction ?? map.ParentFaction;
        }
    }
}
