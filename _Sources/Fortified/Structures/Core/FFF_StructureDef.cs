// 当白昼倾坠之时
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;

namespace Fortified.Structures
{
    public class FFF_StructureDef : Def, IFFF_Structure, IFFF_PawnProvider, IFFF_TaskProvider
    {
        public IntVec2 size = new IntVec2(5, 5);
        public List<FFF_Element> elements = new List<FFF_Element>();
        public List<string> tags = new List<string>();
        public List<string> zoneTags = new List<string>(); // 适用区域标签
        public float baseWeight = 1f; // 基础出现权重
        public int frontDist = 0; // 离正门距离
        public bool disableSuggestedRoof = false; // 是否禁止引擎自动建屋顶（当有单独导出屋顶时设为true）

        public int FrontDist => frontDist;
        public IntVec2 Size => size;

        public IntVec2 GetSize(Rot4 rot)
        {
            return rot.IsHorizontal ? new IntVec2(size.z, size.x) : size;
        }

        [Unsaved]
        private Sketch cachedSketch;

        public Sketch GetSketch()
        {
            if (cachedSketch == null)
            {
                cachedSketch = new Sketch();
                if (elements != null)
                {
                    foreach (var element in elements) element.AddToSketch(cachedSketch);
                }
            }
            return cachedSketch.DeepCopy();
        }

        public List<FFF_PawnGenRequest> GetPawns()
        {
            List<FFF_PawnGenRequest> pawns = new List<FFF_PawnGenRequest>();
            if (elements != null)
            {
                foreach (var element in elements)
                {
                    if (element is IFFF_PawnProvider provider)
                    {
                        pawns.AddRange(provider.GetPawns(Rot4.North, IntVec3.Zero));
                    }
                }
            }
            return pawns;
        }

        public List<IFFF_GenerationTask> GetGenerationTasks()
        {
            List<IFFF_GenerationTask> tasks = new List<IFFF_GenerationTask>();
            if (elements != null)
            {
                foreach (var element in elements)
                {
                    if (element is IFFF_TaskProvider provider)
                    {
                        tasks.AddRange(provider.GetTasks(Rot4.North, IntVec3.Zero));
                    }
                }
            }
            return tasks;
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

        /// <summary>
        /// 載入期佈局檢查：找出同一格被兩個電力 transmitter（導線／電池／發電機）占用的情形。
        /// 這種錯誤在生成時只會表現為 PowerNetGrid 的紅字，很難回推是哪個佈局，
        /// 所以在這裡就直接把 defName 與衝突座標報出來。
        ///
        /// 只檢查靜態元素（Thing / ThingRect / ThingScatter）。SubStructure 與 Scatter
        /// 元素刻意跳過：它們在此階段解析會提早鎖定 RandomSubStructure 的隨機結果，
        /// 而被引用的子結構本身也會各自跑一次這個檢查。
        ///
        /// Load-time layout check for two power transmitters sharing a cell. At generation
        /// time this only shows up as a PowerNetGrid error with no hint as to which layout
        /// caused it, so report defName plus the offending cells here instead.
        /// Only static elements are inspected; SubStructure/Scatter elements are skipped on
        /// purpose, since resolving them here would freeze RandomSubStructure's roll early
        /// and each referenced sub-structure is validated on its own anyway.
        /// </summary>
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;

            Dictionary<IntVec3, ThingDef> claimed = new Dictionary<IntVec3, ThingDef>();
            if (elements == null) yield break;

            foreach (FFF_Element element in elements)
            {
                foreach (var placement in StaticTransmitterPlacements(element))
                {
                    foreach (IntVec3 c in GenAdj.OccupiedRect(placement.pos, placement.rot, placement.def.size))
                    {
                        if (claimed.TryGetValue(c, out ThingDef other))
                        {
                            yield return $"two power transmitters occupy cell {c}: {other.defName} and {placement.def.defName}. " +
                                         "Two transmitters can't share a cell — PowerNetGrid will error at map generation.";
                        }
                        else
                        {
                            claimed[c] = placement.def;
                        }
                    }
                }
            }
        }

        private static IEnumerable<(ThingDef def, IntVec3 pos, Rot4 rot)> StaticTransmitterPlacements(FFF_Element element)
        {
            switch (element)
            {
                case FFF_Element_Thing t when t.def != null && t.def.EverTransmitsPower:
                    yield return (t.def, t.pos, t.rot);
                    break;

                case FFF_Element_ThingRect r when r.def != null && r.def.EverTransmitsPower:
                    foreach (IntVec3 p in new CellRect(r.pos.x, r.pos.z, r.size.x, r.size.z))
                        yield return (r.def, p, r.rot);
                    break;

                case FFF_Element_ThingScatter s when s.def != null && s.def.EverTransmitsPower && !s.posList.NullOrEmpty():
                    foreach (IntVec3 p in s.posList)
                        yield return (s.def, p, s.rot);
                    break;
            }
        }
    }
}
