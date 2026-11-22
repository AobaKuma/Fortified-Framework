// 当白昼倾坠之时
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.Sound;
using static UnityEngine.GridBrushBase;

namespace Fortified.Structures
{
    public static class FFF_ExportUtility
    {
        public class ExportOptions
        {
            public bool includeTerrain = true;
            public bool includeRoofs = true;
            public bool includeTerrainColors = true;
            public bool includeThings = true;
            public bool includeExportTools = true;
            public string defName = "NewStructure";
            public HashSet<Def> excludedDefs = new HashSet<Def>();
        }

        public static string ExportToXML(CellRect rect, Map map, ExportOptions options)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
            sb.AppendLine("<Defs>");
            sb.AppendLine("  <Fortified.Structures.FFF_StructureDef>");
            sb.AppendLine($"    <defName>{options.defName}</defName>");
            sb.AppendLine($"    <size>({rect.Width}, {rect.Height})</size>");

            if (options.includeRoofs)
            {
                // 如果导出了精准屋顶区域，则关闭引擎的自动处理
                sb.AppendLine("    <disableSuggestedRoof>True</disableSuggestedRoof>");
            }

            sb.AppendLine("    <elements>");

            IntVec3 origin = rect.Min;

            // 1. 导出建筑/物品 (带有横向合并优化)
            if (options.includeThings)
            {
                ExportThingsOptimized(rect, map, origin, options, sb);

                // 1b. 物件之间的连结（IFFF_StructureLinkable，例如船体骨架的结构桩钢梁）
                FFF_StructureLinkUtility.ExportLinks(CollectExportedThings(rect, map, options), origin, sb);
            }

            // 2. 导出地形 (带有横向合并优化)
            if (options.includeTerrain)
            {
                ExportTerrainOptimized(rect, map, origin, options, sb);
            }

            // 3. 导出颜色 (带有横向合并优化)
            if (options.includeTerrainColors)
            {
                ExportColorsOptimized(rect, map, origin, options, sb);
            }

            // 4. 导出屋顶 (带有横向合并优化)
            if (options.includeRoofs)
            {
                ExportRoofsOptimized(rect, map, origin, options, sb);
            }

            // 5. Exporting these last since they should be generated last
            if (options.includeExportTools)
            {
				foreach (IntVec3 cell in rect)
				{
					ExportTool t = cell.GetFirstThing<ExportTool>(map);
                    if(t == null)
                    {
                        continue;
                    }
					t.ExportToXML(origin, sb);
				}
			}

            sb.AppendLine("    </elements>");
            sb.AppendLine("  </Fortified.Structures.FFF_StructureDef>");
            sb.AppendLine("</Defs>");

            return sb.ToString();
        }

        private static void ExportThingsOptimized(CellRect rect, Map map, IntVec3 origin, ExportOptions options, StringBuilder sb)
        {
            var rows = CollectThingsIntoRows(rect, map, origin, options);

            // 分配 1D 线段
            var segments = new Dictionary<(ThingDef, ThingDef, Rot4, float?, int, int), List<int>>();
            var isolatedPoints = new Dictionary<(ThingDef, ThingDef, Rot4, float?), List<IntVec3>>(); // 新增加：孤立散点的收集器

            foreach (var kvp in rows)
            {
                var key = kvp.Key;
                int z = key.Item5;
                var xList = kvp.Value.OrderBy(x => x).ToList();

                int startX = xList[0], lastX = xList[0];
                for (int i = 1; i <= xList.Count; i++)
                {
                    if (i < xList.Count && xList[i] == lastX + 1) lastX = xList[i];
                    else
                    {
                        var segKey = (key.Item1, key.Item2, key.Item3, key.Item4, startX, lastX);
                        if (!segments.ContainsKey(segKey)) segments[segKey] = new List<int>();
                        segments[segKey].Add(z);

                        if (i < xList.Count) { startX = xList[i]; lastX = xList[i]; }
                    }
                }
            }

            foreach (var kvp in segments)
            {
                var key = kvp.Key;
                var zList = kvp.Value.OrderBy(z => z).ToList();

                int startZ = zList[0], lastZ = zList[0];
                for (int i = 1; i <= zList.Count; i++)
                {
                    if (i < zList.Count && zList[i] == lastZ + 1) lastZ = zList[i];
                    else
                    {
                        if (key.Item5 == key.Item6 && startZ == lastZ)
                        {
                            var ptKey = (key.Item1, key.Item2, key.Item3, key.Item4);
                            if (!isolatedPoints.ContainsKey(ptKey)) isolatedPoints[ptKey] = new List<IntVec3>();
                            isolatedPoints[ptKey].Add(new IntVec3(key.Item5, 0, startZ));
                        }
                        else
                        {
                            AppendThing2DXML((key.Item1, key.Item2, key.Item3, key.Item4), key.Item5, key.Item6, startZ, lastZ, sb);
                        }
                        if (i < zList.Count) { startZ = zList[i]; lastZ = zList[i]; }
                    }
                }
            }

            // 执行散点写入
            foreach (var kvp in isolatedPoints)
            {
                if (kvp.Value.Count == 1)
                {
                    // 依然只有一个点，正常输出
                    AppendThing2DXML(kvp.Key, kvp.Value[0].x, kvp.Value[0].x, kvp.Value[0].z, kvp.Value[0].z, sb);
                }
                else
                {
                    sb.AppendLine("      <li Class=\"Fortified.Structures.FFF_Element_ThingScatter\">");
                    sb.AppendLine($"        <def>{kvp.Key.Item1.defName}</def>");
                    if (kvp.Key.Item3 != Rot4.North) sb.AppendLine($"        <rot>{kvp.Key.Item3.AsInt}</rot>");
                    if (kvp.Key.Item2 != null) sb.AppendLine($"        <stuff>{kvp.Key.Item2.defName}</stuff>");
                    if (kvp.Key.Item4.HasValue) sb.AppendLine($"        <targetTemperature>{kvp.Key.Item4.Value}</targetTemperature>");
                    sb.AppendLine("        <posList>");
                    foreach (var pt in kvp.Value)
                    {
                        sb.AppendLine($"          <li>({pt.x}, 0, {pt.z})</li>");
                    }
                    sb.AppendLine("        </posList>");
                    sb.AppendLine("      </li>");
                }
            }
        }

        private static Dictionary<(ThingDef, ThingDef, Rot4, float?, int), List<int>> CollectThingsIntoRows(CellRect rect, Map map, IntVec3 origin, ExportOptions options)
        {
            var rows = new Dictionary<(ThingDef, ThingDef, Rot4, float?, int), List<int>>();
            var processed = new HashSet<Thing>();

            foreach (IntVec3 cell in rect)
            {
                foreach (Thing thing in cell.GetThingList(map))
                {
                    if (ShouldSkipThing(thing, options)) continue;
                    if (!processed.Add(thing)) continue; // 防重：多格建筑仅被处理一次

                    float? targetTemp = null;
                    var tempComp = thing.TryGetComp<RimWorld.CompTempControl>();
                    if (tempComp != null) targetTemp = tempComp.targetTemperature;

                    // 修复：多格建筑应该基于自身的基准坐标记录计算偏移量，而不是被扫描到的格子
                    IntVec3 rel = thing.Position - origin;
                    var key = (thing.def, thing.Stuff, thing.Rotation, targetTemp, rel.z);
                    if (!rows.ContainsKey(key)) rows[key] = new List<int>();
                    rows[key].Add(rel.x);
                }
            }
            return rows;
        }

        private static HashSet<Thing> CollectExportedThings(CellRect rect, Map map, ExportOptions options)
        {
            var things = new HashSet<Thing>();
            foreach (IntVec3 cell in rect)
            {
                foreach (Thing thing in cell.GetThingList(map))
                {
                    if (!ShouldSkipThing(thing, options)) things.Add(thing);
                }
            }
            return things;
        }

        private static bool ShouldSkipThing(Thing thing, ExportOptions options)
        {
            if (options.excludedDefs.Contains(thing.def)) return true;
            if (thing.def.category != ThingCategory.Building && thing.def.category != ThingCategory.Item && thing.def.category != ThingCategory.Plant) return true;
            if (thing.def.building?.isNaturalRock == true) return true;
			return thing is Blueprint || thing is Frame;
        }

        private static void AppendThing2DXML((ThingDef def, ThingDef stuff, Rot4 rot, float? temp) key, int startX, int lastX, int startZ, int lastZ, StringBuilder sb)
        {
            bool isRect = (startX != lastX) || (startZ != lastZ);
            sb.AppendLine(isRect ? "      <li Class=\"Fortified.Structures.FFF_Element_ThingRect\">" : "      <li Class=\"Fortified.Structures.FFF_Element_Thing\">");
            sb.AppendLine($"        <def>{key.def.defName}</def>");
            sb.AppendLine($"        <pos>({startX}, 0, {startZ})</pos>");
            if (isRect) sb.AppendLine($"        <size>({lastX - startX + 1}, {lastZ - startZ + 1})</size>");
            if (key.rot != Rot4.North) sb.AppendLine($"        <rot>{key.rot.AsInt}</rot>");
            if (key.stuff != null) sb.AppendLine($"        <stuff>{key.stuff.defName}</stuff>");
            if (key.temp.HasValue) sb.AppendLine($"        <targetTemperature>{key.temp.Value}</targetTemperature>");
            sb.AppendLine("      </li>");
        }

        private static void ExportTerrainOptimized(CellRect rect, Map map, IntVec3 origin, ExportOptions options, StringBuilder sb)
        {
            var rows = new Dictionary<(TerrainDef, int), List<int>>();
            var isolatedPoints = new Dictionary<TerrainDef, List<IntVec3>>(); // 地形散点收集器

            foreach (IntVec3 cell in rect)
            {
                TerrainDef t = cell.GetTerrain(map);
                if (options.excludedDefs.Contains(t)) continue;

                IntVec3 rel = cell - origin;
                var key = (t, rel.z);
                if (!rows.ContainsKey(key)) rows[key] = new List<int>();
                rows[key].Add(rel.x);
            }

            var segments = new Dictionary<(TerrainDef, int, int), List<int>>();
            foreach (var kvp in rows)
            {
                var key = kvp.Key;
                int z = key.Item2;
                var xList = kvp.Value.OrderBy(x => x).ToList();

                int startX = xList[0], lastX = xList[0];
                for (int i = 1; i <= xList.Count; i++)
                {
                    if (i < xList.Count && xList[i] == lastX + 1) lastX = xList[i];
                    else
                    {
                        var segKey = (key.Item1, startX, lastX);
                        if (!segments.ContainsKey(segKey)) segments[segKey] = new List<int>();
                        segments[segKey].Add(z);

                        if (i < xList.Count) { startX = xList[i]; lastX = xList[i]; }
                    }
                }
            }

            foreach (var kvp in segments)
            {
                var key = kvp.Key;
                var zList = kvp.Value.OrderBy(z => z).ToList();

                int startZ = zList[0], lastZ = zList[0];
                for (int i = 1; i <= zList.Count; i++)
                {
                    if (i < zList.Count && zList[i] == lastZ + 1) lastZ = zList[i];
                    else
                    {
                        bool isRect = (key.Item2 != key.Item3) || (startZ != lastZ);

                        if (!isRect)
                        {
                            if (!isolatedPoints.ContainsKey(key.Item1)) isolatedPoints[key.Item1] = new List<IntVec3>();
                            isolatedPoints[key.Item1].Add(new IntVec3(key.Item2, 0, startZ));
                        }
                        else
                        {
                            sb.AppendLine("      <li Class=\"Fortified.Structures.FFF_Element_TerrainRect\">");
                            sb.AppendLine($"        <def>{key.Item1.defName}</def>");
                            sb.AppendLine($"        <pos>({key.Item2}, 0, {startZ})</pos>");
                            sb.AppendLine($"        <size>({key.Item3 - key.Item2 + 1}, {lastZ - startZ + 1})</size>");
                            sb.AppendLine("      </li>");
                        }

                        if (i < zList.Count) { startZ = zList[i]; lastZ = zList[i]; }
                    }
                }
            }

            // 输出地形散点
            foreach (var kvp in isolatedPoints)
            {
                if (kvp.Value.Count == 1)
                {
                    sb.AppendLine("      <li Class=\"Fortified.Structures.FFF_Element_Terrain\">");
                    sb.AppendLine($"        <def>{kvp.Key.defName}</def>");
                    sb.AppendLine($"        <pos>({kvp.Value[0].x}, 0, {kvp.Value[0].z})</pos>");
                    sb.AppendLine("      </li>");
                }
                else
                {
                    sb.AppendLine("      <li Class=\"Fortified.Structures.FFF_Element_TerrainScatter\">");
                    sb.AppendLine($"        <def>{kvp.Key.defName}</def>");
                    sb.AppendLine("        <posList>");
                    foreach (var pt in kvp.Value)
                    {
                        sb.AppendLine($"          <li>({pt.x}, 0, {pt.z})</li>");
                    }
                    sb.AppendLine("        </posList>");
                    sb.AppendLine("      </li>");
                }
            }
        }

        private static void ExportColorsOptimized(CellRect rect, Map map, IntVec3 origin, ExportOptions options, StringBuilder sb)
        {
            var rows = new Dictionary<(ColorDef, int), List<int>>();
            var isolatedPoints = new Dictionary<ColorDef, List<IntVec3>>(); // 颜色散点收集器

            foreach (IntVec3 cell in rect)
            {
                ColorDef color = map.terrainGrid.ColorAt(cell);
                if (color == null) continue;
                IntVec3 rel = cell - origin;
                var key = (color, rel.z);
                if (!rows.ContainsKey(key)) rows[key] = new List<int>();
                rows[key].Add(rel.x);
            }

            var segments = new Dictionary<(ColorDef, int, int), List<int>>();
            foreach (var kvp in rows)
            {
                var key = kvp.Key;
                int z = key.Item2;
                var xList = kvp.Value.OrderBy(x => x).ToList();

                int startX = xList[0], lastX = xList[0];
                for (int i = 1; i <= xList.Count; i++)
                {
                    if (i < xList.Count && xList[i] == lastX + 1) lastX = xList[i];
                    else
                    {
                        var segKey = (key.Item1, startX, lastX);
                        if (!segments.ContainsKey(segKey)) segments[segKey] = new List<int>();
                        segments[segKey].Add(z);

                        if (i < xList.Count) { startX = xList[i]; lastX = xList[i]; }
                    }
                }
            }

            foreach (var kvp in segments)
            {
                var key = kvp.Key;
                var zList = kvp.Value.OrderBy(z => z).ToList();

                int startZ = zList[0], lastZ = zList[0];
                for (int i = 1; i <= zList.Count; i++)
                {
                    if (i < zList.Count && zList[i] == lastZ + 1) lastZ = zList[i];
                    else
                    {
                        bool isRect = (key.Item2 != key.Item3) || (startZ != lastZ);

                        if (!isRect)
                        {
                            if (!isolatedPoints.ContainsKey(key.Item1)) isolatedPoints[key.Item1] = new List<IntVec3>();
                            isolatedPoints[key.Item1].Add(new IntVec3(key.Item2, 0, startZ));
                        }
                        else
                        {
                            sb.AppendLine("      <li Class=\"Fortified.Structures.FFF_Element_TerrainColorRect\">");
                            sb.AppendLine($"        <color>{key.Item1.defName}</color>");
                            sb.AppendLine($"        <pos>({key.Item2}, 0, {startZ})</pos>");
                            sb.AppendLine($"        <size>({key.Item3 - key.Item2 + 1}, {lastZ - startZ + 1})</size>");
                            sb.AppendLine("      </li>");
                        }

                        if (i < zList.Count) { startZ = zList[i]; lastZ = zList[i]; }
                    }
                }
            }

            // 输出颜色散点
            foreach (var kvp in isolatedPoints)
            {
                if (kvp.Value.Count == 1)
                {
                    sb.AppendLine("      <li Class=\"Fortified.Structures.FFF_Element_TerrainColor\">");
                    sb.AppendLine($"        <color>{kvp.Key.defName}</color>");
                    sb.AppendLine($"        <pos>({kvp.Value[0].x}, 0, {kvp.Value[0].z})</pos>");
                    sb.AppendLine("      </li>");
                }
                else
                {
                    sb.AppendLine("      <li Class=\"Fortified.Structures.FFF_Element_TerrainColorScatter\">");
                    sb.AppendLine($"        <color>{kvp.Key.defName}</color>");
                    sb.AppendLine("        <posList>");
                    foreach (var pt in kvp.Value)
                    {
                        sb.AppendLine($"          <li>({pt.x}, 0, {pt.z})</li>");
                    }
                    sb.AppendLine("        </posList>");
                    sb.AppendLine("      </li>");
                }
            }
        }

        private static void ExportRoofsOptimized(CellRect rect, Map map, IntVec3 origin, ExportOptions options, StringBuilder sb)
        {
            var rows = new Dictionary<(RoofDef, int), List<int>>();
            var isolatedPoints = new Dictionary<RoofDef, List<IntVec3>>(); // 屋顶散点收集器

            foreach (IntVec3 cell in rect)
            {
                RoofDef r = cell.GetRoof(map);
                if (r == null || options.excludedDefs.Contains(r)) continue;

                IntVec3 rel = cell - origin;
                var key = (r, rel.z);
                if (!rows.ContainsKey(key)) rows[key] = new List<int>();
                rows[key].Add(rel.x);
            }

            var segments = new Dictionary<(RoofDef, int, int), List<int>>();
            foreach (var kvp in rows)
            {
                var key = kvp.Key;
                int z = key.Item2;
                var xList = kvp.Value.OrderBy(x => x).ToList();

                int startX = xList[0], lastX = xList[0];
                for (int i = 1; i <= xList.Count; i++)
                {
                    if (i < xList.Count && xList[i] == lastX + 1) lastX = xList[i];
                    else
                    {
                        var segKey = (key.Item1, startX, lastX);
                        if (!segments.ContainsKey(segKey)) segments[segKey] = new List<int>();
                        segments[segKey].Add(z);

                        if (i < xList.Count) { startX = xList[i]; lastX = xList[i]; }
                    }
                }
            }

            foreach (var kvp in segments)
            {
                var key = kvp.Key;
                var zList = kvp.Value.OrderBy(z => z).ToList();

                int startZ = zList[0], lastZ = zList[0];
                for (int i = 1; i <= zList.Count; i++)
                {
                    if (i < zList.Count && zList[i] == lastZ + 1) lastZ = zList[i];
                    else
                    {
                        bool isRect = (key.Item2 != key.Item3) || (startZ != lastZ);

                        if (!isRect)
                        {
                            if (!isolatedPoints.ContainsKey(key.Item1)) isolatedPoints[key.Item1] = new List<IntVec3>();
                            isolatedPoints[key.Item1].Add(new IntVec3(key.Item2, 0, startZ));
                        }
                        else
                        {
                            sb.AppendLine("      <li Class=\"Fortified.Structures.FFF_Element_RoofRect\">");
                            sb.AppendLine($"        <def>{key.Item1.defName}</def>");
                            sb.AppendLine($"        <pos>({key.Item2}, 0, {startZ})</pos>");
                            sb.AppendLine($"        <size>({key.Item3 - key.Item2 + 1}, {lastZ - startZ + 1})</size>");
                            sb.AppendLine("      </li>");
                        }

                        if (i < zList.Count) { startZ = zList[i]; lastZ = zList[i]; }
                    }
                }
            }

            // 输出散点
            foreach (var kvp in isolatedPoints)
            {
                if (kvp.Value.Count == 1)
                {
                    sb.AppendLine("      <li Class=\"Fortified.Structures.FFF_Element_Roof\">");
                    sb.AppendLine($"        <def>{kvp.Key.defName}</def>");
                    sb.AppendLine($"        <pos>({kvp.Value[0].x}, 0, {kvp.Value[0].z})</pos>");
                    sb.AppendLine("      </li>");
                }
                else
                {
                    sb.AppendLine("      <li Class=\"Fortified.Structures.FFF_Element_RoofScatter\">");
                    sb.AppendLine($"        <def>{kvp.Key.defName}</def>");
                    sb.AppendLine("        <posList>");
                    foreach (var pt in kvp.Value)
                    {
                        sb.AppendLine($"          <li>({pt.x}, 0, {pt.z})</li>");
                    }
                    sb.AppendLine("        </posList>");
                    sb.AppendLine("      </li>");
                }
            }
        }
	}
}
