// 当白昼倾坠之时
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;

namespace Fortified.Structures
{
    // 复合结构生成工具
    public static class CompoundStructureUtility
    {
        public static void Generate(FFF_CompoundStructureDef def, IntVec3 center, Map map, Faction faction = null)
        {
            if (def == null || map == null) return;

            CellRect settlementRect = CellRect.CenteredOn(center, def.settlementSize.x, def.settlementSize.z);
            List<CellRect> usedRects = new List<CellRect>();

            // 1. 清理区域
            if (def.clearArea) ClearSettlementArea(map, settlementRect);

            // 2. 生成中心结构
            CellRect centerRect = GenerateCenterStructure(def, center, map, faction, usedRects);
            if (centerRect == CellRect.Empty) return;

            // 3. 生成道路
            if (def.roadConfig != null) GenerateRoads(def.roadConfig, center, settlementRect, map);

            // 4. 生成周边结构
            GeneratePeripheralStructures(def, center, settlementRect, map, faction, usedRects);

            // 5. 生成防御设施
            if (def.defenseConfig != null) GenerateDefenses(def.defenseConfig, settlementRect, map, faction);

            // 6. 完成处理
            FinishGeneration(map, settlementRect);
        }

        private static void ClearSettlementArea(Map map, CellRect rect)
        {
            foreach (IntVec3 c in rect)
            {
                if (!c.InBounds(map)) continue;
                
                List<Thing> things = c.GetThingList(map).ToList();
                foreach (Thing t in things)
                {
                    if (t.def.destroyable && IsRemovableForSettlement(t.def))
                        t.Destroy(DestroyMode.Vanish);
                }
            }
        }

        private static bool IsRemovableForSettlement(ThingDef def)
        {
            return def.category == ThingCategory.Building ||
                   def.category == ThingCategory.Plant ||
                   def.category == ThingCategory.Filth ||
                   (def.thingCategories != null && def.thingCategories.Contains(ThingCategoryDefOf.Chunks));
        }

        private static CellRect GenerateCenterStructure(FFF_CompoundStructureDef def, IntVec3 center, Map map, Faction faction, List<CellRect> usedRects)
        {
            if (def.centerStructure == null) return CellRect.Empty;

            IFFF_Structure structure = ResolveCenterStructure(def.centerStructure);
            if (structure == null)
            {
                Log.Warning("[FFF] CompoundStructure: 无法解析中心结构");
                return CellRect.Empty;
            }

            Sketch sketch = structure.GetSketch();
            CellRect structRect = sketch.OccupiedRect.MovedBy(center - sketch.OccupiedRect.CenterCell);
            
            // 扩展留空区域
            CellRect reservedRect = structRect.ExpandedBy(def.centerStructure.spaceAround);
            usedRects.Add(reservedRect);

            FFF_StructureUtility.Generate(structure, center, map, faction, Rot4.North, reconnectPower: false);
            return structRect;
        }

        private static IFFF_Structure ResolveCenterStructure(CenterStructureConfig config)
        {
            if (config.structureDef != null) return config.structureDef;
            if (!config.structureTag.NullOrEmpty())
                return FindStructureByTag(config.structureTag);
            return null;
        }

        private static void GeneratePeripheralStructures(FFF_CompoundStructureDef def, IntVec3 center, CellRect settlementRect, Map map, Faction faction, List<CellRect> usedRects)
        {
            if (def.peripheralStructures.NullOrEmpty()) return;

            foreach (var config in def.peripheralStructures)
            {
                int count = config.count.RandomInRange;
                for (int i = 0; i < count; i++)
                {
                    TryPlacePeripheralStructure(config, center, settlementRect, map, faction, usedRects);
                }
            }
        }

        private static void TryPlacePeripheralStructure(PeripheralStructureConfig config, IntVec3 center, CellRect settlementRect, Map map, Faction faction, List<CellRect> usedRects)
        {
            IFFF_Structure structure = ResolvePeripheralStructure(config);
            if (structure == null) return;

            Sketch sketch = structure.GetSketch();
            IntVec2 size = sketch.OccupiedRect.Size;

            // 尝试找到有效位置
            for (int attempt = 0; attempt < 50; attempt++)
            {
                IntVec3 candidate = FindCandidatePosition(center, config, settlementRect, map);
                if (!candidate.IsValid) continue;

                // 计算朝向
                Rot4 rotation = Rot4.North;
                if (config.facingCenter)
                    rotation = CalculateFacingRotation(candidate, center);

                // 计算实际占用区域
                Sketch rotatedSketch = sketch.DeepCopy();
                if (rotation != Rot4.North) rotatedSketch.Rotate(rotation);
                
                CellRect structRect = rotatedSketch.OccupiedRect.MovedBy(candidate - rotatedSketch.OccupiedRect.CenterCell);
                CellRect reservedRect = structRect.ExpandedBy(config.spaceAround);

                // 检查是否与已有结构冲突
                if (IsRectValid(reservedRect, settlementRect, usedRects, map))
                {
                    usedRects.Add(reservedRect);
                    FFF_StructureUtility.Generate(structure, candidate, map, faction, rotation, reconnectPower: false);
                    return;
                }
            }
        }

        private static IFFF_Structure ResolvePeripheralStructure(PeripheralStructureConfig config)
        {
            if (config.structureDef != null) return config.structureDef;
            if (!config.structureTag.NullOrEmpty())
                return FindStructureByTag(config.structureTag);
            return null;
        }

        private static IntVec3 FindCandidatePosition(IntVec3 center, PeripheralStructureConfig config, CellRect settlementRect, Map map)
        {
            float angle = Rand.Range(0f, 360f);
            float dist = Rand.Range(config.minDistFromCenter, config.maxDistFromCenter);
            
            int x = center.x + Mathf.RoundToInt(Mathf.Cos(angle * Mathf.Deg2Rad) * dist);
            int z = center.z + Mathf.RoundToInt(Mathf.Sin(angle * Mathf.Deg2Rad) * dist);
            IntVec3 candidate = new IntVec3(x, 0, z);

            if (!candidate.InBounds(map) || !settlementRect.Contains(candidate))
                return IntVec3.Invalid;

            return candidate;
        }

        // 根据相对位置计算朝向中心的旋转
        private static Rot4 CalculateFacingRotation(IntVec3 pos, IntVec3 center)
        {
            int dx = center.x - pos.x;
            int dz = center.z - pos.z;

            // 判断主方向
            if (Mathf.Abs(dx) > Mathf.Abs(dz))
                return dx > 0 ? Rot4.East : Rot4.West;
            else
                return dz > 0 ? Rot4.North : Rot4.South;
        }

        private static bool IsRectValid(CellRect rect, CellRect bounds, List<CellRect> usedRects, Map map)
        {
            if (!bounds.Contains(rect.CenterCell)) return false;
            
            foreach (IntVec3 c in rect)
            {
                if (!c.InBounds(map)) return false;
                if (!c.Standable(map) && c.GetEdifice(map) == null) return false;
            }

            foreach (CellRect used in usedRects)
            {
                if (rect.Overlaps(used)) return false;
            }

            return true;
        }

        private static IFFF_Structure FindStructureByTag(string tag)
        {
            // 从 StructureLayoutDef 中查找
            var layoutDef = DefDatabase<StructureLayoutDef>.AllDefs.FirstOrDefault(d => d.tags != null && d.tags.Contains(tag));
            if (layoutDef != null) return layoutDef;

            // 从 FFF_StructureDef 中查找
            var structDef = DefDatabase<FFF_StructureDef>.AllDefs.FirstOrDefault(d => d.tags != null && d.tags.Contains(tag));
            return structDef;
        }

        private static void GenerateRoads(RoadConfig config, IntVec3 center, CellRect rect, Map map)
        {
            if (!config.addMainRoad || config.mainRoadTerrain == null) return;

            // 生成十字主道路
            int halfWidth = config.mainRoadWidth / 2;
            
            // 东西向
            for (int x = rect.minX; x <= rect.maxX; x++)
            {
                for (int w = -halfWidth; w <= halfWidth; w++)
                {
                    IntVec3 c = new IntVec3(x, 0, center.z + w);
                    if (c.InBounds(map)) map.terrainGrid.SetTerrain(c, config.mainRoadTerrain);
                }
            }

            // 南北向
            for (int z = rect.minZ; z <= rect.maxZ; z++)
            {
                for (int w = -halfWidth; w <= halfWidth; w++)
                {
                    IntVec3 c = new IntVec3(center.x + w, 0, z);
                    if (c.InBounds(map)) map.terrainGrid.SetTerrain(c, config.mainRoadTerrain);
                }
            }
        }

        private static void GenerateDefenses(DefenseConfig config, CellRect rect, Map map, Faction faction)
        {
            if (!config.addEdgeDefense) return;

            List<IntVec3> edgeCells = rect.EdgeCells.ToList();

            // 沙袋
            if (config.addSandbags)
            {
                foreach (IntVec3 c in edgeCells)
                {
                    if (c.InBounds(map) && c.Standable(map) && c.GetEdifice(map) == null)
                    {
                        Thing sandbag = ThingMaker.MakeThing(ThingDefOf.Sandbags);
                        if (faction != null) sandbag.SetFactionDirect(faction);
                        GenSpawn.Spawn(sandbag, c, map, WipeMode.VanishOrMoveAside);
                    }
                }
            }

            // 炮塔
            if (config.addTurrets && !config.allowedTurrets.NullOrEmpty())
            {
                int turretCount = Mathf.Max(1, rect.Area / config.cellsPerTurret);
                for (int i = 0; i < turretCount; i++)
                {
                    IntVec3 pos = edgeCells.RandomElement();
                    if (pos.InBounds(map) && pos.Standable(map) && pos.GetEdifice(map) == null)
                    {
                        ThingDef turretDef = config.allowedTurrets.RandomElement();
                        Thing turret = ThingMaker.MakeThing(turretDef, ThingDefOf.Steel);
                        if (faction != null) turret.SetFactionDirect(faction);
                        GenSpawn.Spawn(turret, pos, map, WipeMode.VanishOrMoveAside);
                    }
                }
            }
        }

        private static void FinishGeneration(Map map, CellRect rect)
        {
            // 解雾
            foreach (IntVec3 c in rect)
            {
                if (c.InBounds(map)) map.fogGrid.Unfog(c);
            }

            // 重连电网
            map.powerNetManager?.UpdatePowerNetsAndConnections_First();
        }
    }
}
