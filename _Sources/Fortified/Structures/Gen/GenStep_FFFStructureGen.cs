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
            // GenStepDef.genStep 在整場遊戲中是「單一共用實例」，同一個 GenStepDef 生成的
            // 每一張地圖都跑在同一個物件上。不先歸零的話，上一張地圖的落點會被下一張沿用 ——
            // 例如衛星散布會繞著另一張（可能更大的）地圖的座標鋪開，整圈被推向邊緣甚至全部落空。
            //
            // GenStepDef.genStep is a single shared instance for the whole game, so every map
            // built from the same def runs through the same object. Without this reset the
            // previous map's footprint leaks into the next one and the satellite ring is laid
            // out around stale (possibly out-of-bounds) coordinates.
            ResetLastStructure();

            if (map == null) return;

            IntVec3 center = CalculateCenter(map);
            Faction faction = map.ParentFaction ?? parms.sitePart?.site?.Faction;

            // 优先尝试复合结构
            FFF_CompoundStructureDef compoundDef = ResolveCompoundDef();
            if (compoundDef != null)
            {
                CellRect compoundRect = CompoundStructureUtility.Generate(compoundDef, center, map, faction);
                if (compoundRect.Width > 0 && compoundRect.Height > 0)
                {
                    lastStructureDef = compoundDef;
                    lastStructureCenter = compoundRect.CenterCell;
                    lastStructureRot = Rot4.North;
                    lastStructureRect = compoundRect;
                    hasLastStructure = true;

                    // 複合結構過去會直接 return，導致 filthTypes 與 symbolResolvers 靜默失效。
                    // The compound path used to return early, silently dropping filth and resolvers.
                    HandlePostScatter(map, compoundRect);
                }
                return;
            }

            // 回退到单一结构
            IFFF_Structure def = ResolveStructureDef();
            if (def == null) return;

            Rot4 rot = randomRotation ? Rot4.Random : Rot4.North;

            // 把落點推回安全帶內。超界的部分只會在 SpawnThings 被逐格丟棄（DevMode 才有警告），
            // 成品看起來就是被切齊在地圖邊上，所以寧可整體平移也不要靜默裁切。
            // Push the placement back inside the usable area: out-of-bounds cells are dropped
            // one by one in SpawnThings, which reads as a structure sheared off at the border.
            center = ClampToFit(map, def, center, rot);

            lastStructureDef = def;
            lastStructureCenter = center;
            lastStructureRot = rot;
            lastStructureRect = FFF_StructureUtility.FootprintAt(def, center, rot);
            hasLastStructure = true;

            // reconnectPower: false —— 生成期不強制刷新電網，見 Generate 的參數說明。
            FFF_StructureUtility.Generate(def, center, map, faction, rot, reconnectPower: false);
            HandlePostScatter(map, lastStructureRect);
        }

        /// <summary>
        /// 清掉上一次生成留下的落點。每次 Generate 進來的第一件事。
        /// Clears the previous run's placement; the first thing Generate does.
        /// </summary>
        protected void ResetLastStructure()
        {
            lastStructureDef = null;
            lastStructureCenter = IntVec3.Invalid;
            lastStructureRot = Rot4.North;
            lastStructureRect = default;
            hasLastStructure = false;
        }

        /// <summary>
        /// 把 center 平移到「整個足跡都落在地圖安全帶內」。
        /// 足跡本身就大於安全帶時救不了，只保證置中並留下警告，讓裁切至少是對稱的。
        ///
        /// Shifts <paramref name="center"/> so the whole footprint lands inside the usable
        /// area. When the footprint is larger than the map itself nothing can save it — it is
        /// centred and a warning is logged, so at least the clipping is symmetric.
        /// </summary>
        protected static IntVec3 ClampToFit(Map map, IFFF_Structure def, IntVec3 center, Rot4 rot)
        {
            if (map == null || def == null) return center;

            CellRect foot = FFF_StructureUtility.FootprintAt(def, center, rot);
            CellRect safe = FFF_StructureUtility.UsableRect(map);
            if (foot.Width <= 0 || foot.Height <= 0 || safe.Width <= 0 || safe.Height <= 0) return center;

            int dx;
            if (foot.Width > safe.Width) dx = safe.CenterCell.x - foot.CenterCell.x;
            else if (foot.minX < safe.minX) dx = safe.minX - foot.minX;
            else if (foot.maxX > safe.maxX) dx = safe.maxX - foot.maxX;
            else dx = 0;

            int dz;
            if (foot.Height > safe.Height) dz = safe.CenterCell.z - foot.CenterCell.z;
            else if (foot.minZ < safe.minZ) dz = safe.minZ - foot.minZ;
            else if (foot.maxZ > safe.maxZ) dz = safe.maxZ - foot.maxZ;
            else dz = 0;

            if (foot.Width > safe.Width || foot.Height > safe.Height)
            {
                Log.Warning($"[FortifiedFramework] {(def as Def)?.defName ?? "structure"}: footprint " +
                            $"{foot.Width}x{foot.Height} exceeds the usable area {safe.Width}x{safe.Height} of a " +
                            $"{map.Size.x}x{map.Size.z} map; centred, but the overhang will be clipped.");
            }
            else if (dx != 0 || dz != 0)
            {
                if (Prefs.DevMode)
                    Log.Message($"[FortifiedFramework] {(def as Def)?.defName ?? "structure"}: nudged by ({dx}, {dz}) to clear the map edge.");
            }

            return center + new IntVec3(dx, 0, dz);
        }

        /// <summary>
        /// 依 useTag 或明列的清單取複合結構。
        /// 過去是比對 label.Contains(useTag) —— label 是給玩家看的顯示名稱，任何字面上撞到
        /// useTag 的複合結構都會劫持整條生成路徑，換掉玩家預期的佈局。改為只比對 tags。
        ///
        /// Matches on tags only. It used to test label.Contains(useTag); label is a
        /// player-facing display string, so any incidental substring hit would hijack the
        /// whole generation path and swap out the expected layout.
        /// </summary>
        private FFF_CompoundStructureDef ResolveCompoundDef()
        {
            if (!compoundStructureDefs.NullOrEmpty())
                return compoundStructureDefs.Where(x => x != null).RandomElementWithFallback();

            if (!useTag.NullOrEmpty())
            {
                var matches = DefDatabase<FFF_CompoundStructureDef>.AllDefs
                    .Where(x => x.tags != null && x.tags.Contains(useTag));
                return matches.RandomElementWithFallback();
            }
            return null;
        }

        private void HandlePostScatter(Map map, CellRect rect)
        {
            if (map == null || rect.Width <= 0 || rect.Height <= 0) return;

            // 1. 处理污垢散射
            if (!filthTypes.NullOrEmpty())
            {
                List<ThingDef> filthDefs = filthTypes.Select(x => DefDatabase<ThingDef>.GetNamedSilentFail(x)).Where(x => x != null).ToList();
                if (filthDefs.Count > 0)
                {
                    new Task_ScatterFilth { rect = rect, filthTypes = filthDefs, chance = 0.25f }.Execute(map, IntVec3.Zero, null);
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
    }
}
