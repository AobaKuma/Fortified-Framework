// 当白昼倾坠之时
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Fortified.Structures
{
    // 基础生成任务接口
    public interface IFFF_GenerationTask
    {
        void Execute(Map map, IntVec3 offset, Faction faction);
        IFFF_GenerationTask Transformed(Rot4 rot, IntVec3 offset);
    }

    // 填充容器任务
    public class Task_FillContainer : IFFF_GenerationTask
    {
        public IntVec3 pos;
        public ThingSetMakerDef makerDef;
        public float stackMultiplier = 1f;

        public void Execute(Map map, IntVec3 offset, Faction faction)
        {
            IntVec3 actualPos = pos + offset;
            Building_Crate crate = actualPos.GetFirstThing<Building_Crate>(map);
            if (crate == null) return;

            if (makerDef?.root == null) return;

            ThingSetMakerParams parms = new ThingSetMakerParams();
            List<Thing> things = makerDef.root.Generate(parms);
            foreach (var thing in things)
            {
                thing.stackCount = (int)Math.Min(thing.stackCount * stackMultiplier, thing.def.stackLimit);
                if (!crate.TryAcceptThing(thing))
                {
                    thing.Destroy();
                }
            }
        }

        public IFFF_GenerationTask Transformed(Rot4 rot, IntVec3 offset)
        {
            return new Task_FillContainer { pos = pos.RotatedBy(rot) + offset, makerDef = makerDef, stackMultiplier = stackMultiplier };
        }
    }

    // 操作迫击炮任务
    public class Task_ManMortar : IFFF_GenerationTask
    {
        public IntVec3 pos;
        public FactionDef factionDef;
        public PawnKindDef pawnKindDef;

        public void Execute(Map map, IntVec3 offset, Faction faction)
        {
            IntVec3 actualPos = pos + offset;
            Building_TurretGun turret = actualPos.GetFirstThing<Building_TurretGun>(map);
            if (turret == null || !turret.def.building.IsMortar) return;

            faction = (factionDef != null ? Find.FactionManager.FirstFactionOfDef(factionDef) : turret.Faction) ?? faction;
            if (faction == null || faction == Faction.OfPlayer) return;

            // 寻找操作位置
            IntVec3 manPos = turret.InteractionCell;
            PawnKindDef pk = pawnKindDef ?? faction.RandomPawnKind();
            if (pk == null) return;

            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(pk, faction, PawnGenerationContext.NonPlayer, map.Tile));
            GenSpawn.Spawn(pawn, manPos, map);

            // 指派操作任务
            pawn.mindState.duty = new Verse.AI.PawnDuty(DutyDefOf.ManClosestTurret, actualPos);

            // 生成炮弹
            ThingDef shellDef = TurretGunUtility.TryFindRandomShellDef(turret.def, false, true, true, faction.def.techLevel);
            if (shellDef != null)
            {
                Thing shells = ThingMaker.MakeThing(shellDef);
                shells.stackCount = Rand.Range(5, 10);
                GenPlace.TryPlaceThing(shells, manPos, map, ThingPlaceMode.Near);
            }
        }

        public IFFF_GenerationTask Transformed(Rot4 rot, IntVec3 offset)
        {
            return new Task_ManMortar { pos = pos.RotatedBy(rot) + offset, factionDef = factionDef, pawnKindDef = pawnKindDef };
        }
    }

    // 设置品质与状态任务
    public class Task_SetThingState : IFFF_GenerationTask
    {
        public IntVec3 pos;
        public bool forbidden = true;
        public QualityCategory? quality;
        public ColorDef color;
        public float? plantGrowth;

        public void Execute(Map map, IntVec3 offset, Faction faction)
        {
            IntVec3 actualPos = pos + offset;
            foreach (Thing t in actualPos.GetThingList(map))
            {
                if (t is Plant p && plantGrowth.HasValue)
                {
                    p.Growth = plantGrowth.Value;
                }

                if (t.def.category == ThingCategory.Item)
                {
                    if (forbidden) t.SetForbidden(true, false);
                }

                if (quality.HasValue && t.TryGetComp<CompQuality>() is CompQuality comp)
                {
                    comp.SetQuality(quality.Value, ArtGenerationContext.Outsider);
                }

                if (color != null && t is Building b && t.def.building?.paintable == true)
                {
                    b.ChangePaint(color);
                }
            }
        }

        public IFFF_GenerationTask Transformed(Rot4 rot, IntVec3 offset)
        {
            return new Task_SetThingState { pos = pos.RotatedBy(rot) + offset, forbidden = forbidden, quality = quality, color = color, plantGrowth = plantGrowth };
        }
    }

    // 任务：应用地形染色
    public class Task_ApplyTerrainColor : IFFF_GenerationTask
    {
        public IntVec3 pos;
        public ColorDef color;

        public void Execute(Map map, IntVec3 offset, Faction faction)
        {
            IntVec3 finalPos = pos + offset;
            if (finalPos.InBounds(map) && color != null)
                map.terrainGrid.SetTerrainColor(finalPos, color);
        }

        public IFFF_GenerationTask Transformed(Rot4 rot, IntVec3 offset)
        {
            return new Task_ApplyTerrainColor { pos = pos.RotatedBy(rot) + offset, color = color };
        }
    }

    // 任务：应用多层地形 (用于兼容 KCSG 的 Foundation/Under/Temp 层)
    public class Task_ApplyTerrainLayer : IFFF_GenerationTask
    {
        public IntVec3 pos;
        public TerrainDef terrain;
        public TerrainLayerType layerType;

        public void Execute(Map map, IntVec3 offset, Faction faction)
        {
            IntVec3 finalPos = pos + offset;
            if (!finalPos.InBounds(map) || terrain == null) return;

            switch (layerType)
            {
                case TerrainLayerType.Foundation: map.terrainGrid.SetFoundation(finalPos, terrain); break;
                case TerrainLayerType.Under: map.terrainGrid.SetUnderTerrain(finalPos, terrain); break;
                case TerrainLayerType.Temp: map.terrainGrid.SetTempTerrain(finalPos, terrain); break;
            }
        }

        public IFFF_GenerationTask Transformed(Rot4 rot, IntVec3 offset)
        {
            return new Task_ApplyTerrainLayer { pos = pos.RotatedBy(rot) + offset, terrain = terrain, layerType = layerType };
        }
    }

    // 任务：散布随机污垢
    public class Task_ScatterFilth : IFFF_GenerationTask
    {
        public CellRect rect;
        public List<ThingDef> filthTypes;
        public float chance = 0.2f;

        public void Execute(Map map, IntVec3 offset, Faction faction)
        {
            if (filthTypes.NullOrEmpty()) return;
            CellRect finalRect = rect.MovedBy(offset);
            foreach (IntVec3 c in finalRect)
            {
                if (c.InBounds(map) && Rand.Chance(chance))
                    FilthMaker.TryMakeFilth(c, map, filthTypes.RandomElement());
            }
        }

        public IFFF_GenerationTask Transformed(Rot4 rot, IntVec3 offset)
        {
            IntVec3 c1 = rect.Min.RotatedBy(rot);
            IntVec3 c2 = rect.Max.RotatedBy(rot);
            // 确保 min <= max 以构造有效矩形
            IntVec3 newMin = new IntVec3(Mathf.Min(c1.x, c2.x), 0, Mathf.Min(c1.z, c2.z));
            IntVec3 newMax = new IntVec3(Mathf.Max(c1.x, c2.x), 0, Mathf.Max(c1.z, c2.z));
            return new Task_ScatterFilth { rect = CellRect.FromLimits(newMin, newMax).MovedBy(offset), filthTypes = filthTypes, chance = chance };
        }
    }

    public enum TerrainLayerType { Foundation, Under, Temp }

    // 生成电缆任务 - 必须在建筑生成后执行
    public class Task_SpawnConduit : IFFF_GenerationTask
    {
        public IntVec3 pos;

        public void Execute(Map map, IntVec3 offset, Faction faction)
        {
            IntVec3 actualPos = pos + offset;
            if (!actualPos.InBounds(map)) return;

            // 检查是否已有电缆
            if (actualPos.GetFirstThing(map, ThingDefOf.PowerConduit) != null) return;

            Thing conduit = ThingMaker.MakeThing(ThingDefOf.PowerConduit);
            // 使用 FullRefund 模式确保不会破坏现有建筑
            GenSpawn.Spawn(conduit, actualPos, map, WipeMode.FullRefund);
        }

        public IFFF_GenerationTask Transformed(Rot4 rot, IntVec3 offset)
        {
            return new Task_SpawnConduit { pos = pos.RotatedBy(rot) + offset };
        }
    }

    // 应用屋顶任务
    public class Task_ApplyRoof : IFFF_GenerationTask
    {
        public IntVec3 pos;
        public RoofDef roofDef;
        public bool force;

        public void Execute(Map map, IntVec3 offset, Faction faction)
        {
            IntVec3 actualPos = pos + offset;
            if (!actualPos.InBounds(map)) return;

            // 如果 force 为 true，或者当前没有屋顶（或者是可覆盖的薄顶），则应用新屋顶
            if (force || actualPos.GetRoof(map) == null || !actualPos.GetRoof(map).isThickRoof)
            {
                map.roofGrid.SetRoof(actualPos, roofDef);
            }
        }

        public IFFF_GenerationTask Transformed(Rot4 rot, IntVec3 offset)
        {
            return new Task_ApplyRoof { pos = pos.RotatedBy(rot) + offset, roofDef = roofDef, force = force };
        }
    }

    // 设置制冷机机等目标温度任务
    public class Task_SetTempControl : IFFF_GenerationTask
    {
        public IntVec3 pos;
        public float targetTemperature;

        public void Execute(Map map, IntVec3 offset, Faction faction)
        {
            IntVec3 actualPos = pos + offset;
            if (!actualPos.InBounds(map)) return;

            foreach (Thing t in actualPos.GetThingList(map))
            {
                var comp = t.TryGetComp<CompTempControl>();
                if (comp != null)
                {
                    comp.targetTemperature = targetTemperature;
                }
            }
        }

        public IFFF_GenerationTask Transformed(Rot4 rot, IntVec3 offset)
        {
            return new Task_SetTempControl { pos = pos.RotatedBy(rot) + offset, targetTemperature = targetTemperature };
        }
    }

	public class Task_SpawnPawnGroup : IFFF_GenerationTask
	{
		public IntVec3 pos;
		public Faction faction;
		public List<PawnKindDef> pawns;
		public string lordTag = "";
		public float sendSignalRadius = -1f;
		public void Execute(Map map, IntVec3 offset, Faction faction)
		{
			faction = this.faction ?? faction;
			IntVec3 actualPos = pos + offset;
			if (!actualPos.InBounds(map)) return;
			List<Pawn> list = new List<Pawn>();
			foreach (PawnKindDef item in pawns)
			{
				Pawn pawn = PawnGenerator.GeneratePawn(item, faction, map.Tile);
				list.Add(pawn);
				GenPlace.TryPlaceThing(pawn, CellFinder.RandomClosewalkCellNear(actualPos, map, 5, c => c.Standable(map)), map, ThingPlaceMode.Near);
			}
			LordMaker.MakeNewLord(faction, new LordJob_DefendCell(actualPos, lordTag) { sendSignalRadius = sendSignalRadius }, map, list);
		}

		public IFFF_GenerationTask Transformed(Rot4 rot, IntVec3 offset)
		{
			return new Task_SpawnPawnGroup { pos = pos.RotatedBy(rot) + offset, pawns = pawns, faction = faction, lordTag = lordTag, sendSignalRadius = sendSignalRadius };
		}
	}

	public class Task_SpawnPawnGroupInRoom : IFFF_GenerationTask
	{
		public IntVec3 pos;
        public Faction faction;
        public List<PawnKindDef> pawns;
        public string lordTag = "";
		public float sendSignalRadius = -1f;
		public void Execute(Map map, IntVec3 offset, Faction faction)
		{
            faction = this.faction ?? faction;
			IntVec3 actualPos = pos + offset;
			if (!actualPos.InBounds(map)) return;
            Room room = actualPos.GetRoom(map);
			List<Pawn> list = new List<Pawn>();
			foreach (PawnKindDef item in pawns)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(item, faction, map.Tile);
				list.Add(pawn);
				GenPlace.TryPlaceThing(pawn, room.Cells.RandomElement(), map, ThingPlaceMode.Near);
			}
            LordMaker.MakeNewLord(faction, new LordJob_DefendRoom(actualPos, lordTag) { sendSignalRadius = sendSignalRadius }, map, list);
		}

		public IFFF_GenerationTask Transformed(Rot4 rot, IntVec3 offset)
		{
			return new Task_SpawnPawnGroupInRoom { pos = pos.RotatedBy(rot) + offset, pawns = pawns, faction = faction, lordTag = lordTag, sendSignalRadius = sendSignalRadius };
		}
	}

	public class Task_LinkAccessKeyWanter : IFFF_GenerationTask
	{
		public IntVec3 activatablePos;
		public IntVec3 wanterPos;
		public void Execute(Map map, IntVec3 offset, Faction faction)
		{
			IntVec3 actualActivatablePos = activatablePos + offset;
			if (!actualActivatablePos.InBounds(map)) return;
			IntVec3 actualWanterPos = wanterPos + offset;
			if (!actualWanterPos.InBounds(map)) return;
			// 兩端皆透過 IAccessKeyActivatable / IAccessKeyWanter 解析，Thing 本體與 ThingComp 都支援
			AccessKeyLinkUtility.TryLinkAt(actualActivatablePos, actualWanterPos, map);
		}

		public IFFF_GenerationTask Transformed(Rot4 rot, IntVec3 offset)
		{
			return new Task_LinkAccessKeyWanter { activatablePos = activatablePos.RotatedBy(rot) + offset, wanterPos = wanterPos.RotatedBy(rot) + offset };
		}
	}
}
