// 当白昼倾坠之时
using System.Collections.Generic;
using RimWorld;
using Verse;
using UnityEngine;

namespace Fortified.Structures
{
    // 复合结构定义：主结构 + 周边子结构
    public class FFF_CompoundStructureDef : Def, IFFF_Structure
    {
        // 聚落总尺寸
        public IntVec2 settlementSize = new IntVec2(60, 60);

        // 標籤，供 GenStep 的 useTag 檢索（與 FFF_StructureDef / StructureLayoutDef 一致）。
        // 過去 GenStep 是拿 label 做子字串比對，任何字面撞名的複合結構都會劫持生成路徑。
        // Tags for GenStep useTag lookup, matching the other structure defs. The GenStep used
        // to substring-match on label, so any incidental name collision hijacked generation.
        public List<string> tags = new List<string>();

        // 中心结构配置
        public CenterStructureConfig centerStructure;
        
        // 周边结构配置
        public List<PeripheralStructureConfig> peripheralStructures = new List<PeripheralStructureConfig>();
        
        // 道路配置
        public RoadConfig roadConfig;
        
        // 防御配置
        public DefenseConfig defenseConfig;
        
        // 生成选项
        public bool avoidWater = true;
        public bool clearArea = true;

        // IFFF_Structure 实现
        public int FrontDist => 0;
        public IntVec2 Size => settlementSize;

        public Sketch GetSketch()
        {
            return new Sketch();
        }

        public List<FFF_PawnGenRequest> GetPawns()
        {
            return new List<FFF_PawnGenRequest>();
        }

        public List<IFFF_GenerationTask> GetGenerationTasks()
        {
            return new List<IFFF_GenerationTask>();
        }

        public List<FFF_PawnGenRequest> GetPawns(Rot4 rot, IntVec3 offset)
        {
            return new List<FFF_PawnGenRequest>();
        }

        public List<IFFF_GenerationTask> GetTasks(Rot4 rot, IntVec3 offset)
        {
            return new List<IFFF_GenerationTask>();
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var err in base.ConfigErrors())
                yield return err;

            if (centerStructure == null)
                yield return "centerStructure cannot be null";
        }
    }

    // 中心结构配置
    public class CenterStructureConfig
    {
        public string structureTag;           // 通过标签查找结构
        public StructureLayoutDef structureDef; // 或直接指定结构
        public int spaceAround = 2;           // 周围留空距离
        public bool forceClean = true;        // 强制清理区域
    }

    // 周边结构配置
    public class PeripheralStructureConfig
    {
        public string structureTag;           // 通过标签查找结构
        public StructureLayoutDef structureDef; // 或直接指定结构
        public IntRange count = new IntRange(1, 4);
        public int minDistFromCenter = 10;    // 距中心最小距离
        public int maxDistFromCenter = 30;    // 距中心最大距离
        public int spaceAround = 1;           // 结构间留空
        public bool facingCenter = true;      // 是否朝向中心
        public float weight = 1f;             // 选择权重
    }

    // 道路配置
    public class RoadConfig
    {
        public bool addMainRoad = false;
        public TerrainDef mainRoadTerrain;
        public int mainRoadWidth = 2;
        
        public bool addLinkRoads = false;
        public TerrainDef linkRoadTerrain;
        public int linkRoadWidth = 1;
    }

    // 防御配置
    public class DefenseConfig
    {
        public bool addEdgeDefense = false;
        public bool addSandbags = false;
        
        public bool addTurrets = false;
        public int cellsPerTurret = 30;
        public List<ThingDef> allowedTurrets = new List<ThingDef>();
        
        public bool addMortars = false;
        public int cellsPerMortar = 75;
        public List<ThingDef> allowedMortars = new List<ThingDef>();
        
        public PawnGroupKindDef guardGroupKind;
        public float guardMultiplier = 1f;
    }
}
