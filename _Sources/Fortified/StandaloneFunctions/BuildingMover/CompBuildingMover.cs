using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using Multiplayer.API;

namespace Fortified;

// 可移动建筑组件
public partial class CompBuildingMover : ThingComp
{
    // 滑动剩余格数
    private float slideProgress;
    // 滑动方向
    private IntVec3 slideDir;
    // 本次滑动总格数
    private int slidePending;
    // 是否正在滑动
    private bool sliding;
    // 轨道当前偏移
    private int trackOffset;
    // 本次滑动是否碾压
    private bool slideCrush;
    // 本次滑动总格数
    private int slideTotalCells;
    // 本次滑动耗时
    private int slideTicksElapsed;
    // 下一格已确认可进入
    private bool nextCellConfirmed;
    // 感应门当前敞开
    private bool doorOpen;
    // 感应门无人计时
    private int doorEmptyTicks;
    // 滑动耗时覆盖
    private int slideDurationOverride;
    // 感应门目标开度
    private int sensorDoorTargetOpen;
    // 来源信号索引
    private int pendingSignalActionIndex = -1;
    // 移动组件已禁用
    private bool disabled;
    // 信号移动后禁用
    private bool pendingDisableAfterMove;
    // 感应门已注册
    private bool sensorDoorRegistered;

    public CompProperties_BuildingMover Props => (CompProperties_BuildingMover)props;

    public bool Sliding => sliding;

    public bool Disabled => disabled;

    // 是否感应门模式
    public bool IsSensorDoor => Props.sensorDoor && !disabled;

    // 感应门是否敞开
    public bool DoorOpen => !disabled && doorOpen;

    // 外部锁定/解锁移动组件 供门禁等外部逻辑调用
    // 锁定后组件完全停摆 感应门注销 建筑退回纯Impassable
    // 滑动中不接受锁定 调用方应稍后重试 返回值表示目标状态是否已达成
    public bool SetDisabled(bool value)
    {
        if (disabled == value) return true;
        if (value && sliding) return false;

        disabled = value;
        if (value)
        {
            // 锁定时收回感应门状态 避免停在半开
            doorOpen = false;
            doorEmptyTicks = 0;
            sensorDoorTargetOpen = 0;
            if (Props.sensorDoor) DeregisterSensorDoorRuntime();
        }
        else if (Props.sensorDoor && parent.Spawned)
        {
            EnsureSensorDoorAnchor();
            RegisterSensorDoorRuntime();
        }
        if (parent.Spawned) RefreshDoorPathCost();
        return true;
    }

    // 获取滑动偏移
    public static Vector3 GetSlideOffset(Thing thing)
    {
        if (!(thing is ThingWithComps twc)) return Vector3.zero;
        foreach (CompBuildingMover c in twc.GetComps<CompBuildingMover>())
        {
            if (c.sliding) return c.SlideDrawOffset;
        }
        return Vector3.zero;
    }

    // 获取感应门组件
    public static CompBuildingMover GetSensorDoor(Thing thing)
    {
        if (!(thing is ThingWithComps twc)) return null;
        foreach (CompBuildingMover c in twc.GetComps<CompBuildingMover>())
        {
            if (c.Props.sensorDoor && !c.disabled) return c;
        }
        return null;
    }

    // 检查建筑滑动
    public static bool AnySliding(Thing thing)
    {
        if (!(thing is ThingWithComps twc)) return false;
        foreach (CompBuildingMover c in twc.GetComps<CompBuildingMover>())
        {
            if (c.sliding) return true;
        }
        return false;
    }

    // 判断开门权限
    public bool PawnCanOpen(Pawn p)
    {
        if (disabled) return false;
        if (p == null || !p.CanOpenDoors) return false;
        if (p.CanOpenAnyDoor) return true;
        Faction owner = parent.Faction;
        if (owner == null) return p.RaceProps.canOpenFactionlessDoors;
        if (p.guest != null && p.guest.Released) return true;
        if (!Props.sensorDoorCheckFaction) return true;
        return GenAI.MachinesLike(owner, p);
    }

    // 判断物理阻挡
    public bool BlocksPawnNow(Pawn p)
    {
        if (disabled) return true;
        return CurrentSensorDoorOpenDistance() <= 0 || sliding;
    }

    // 触发接近开门
    public void NotifyApproachAndOpen(Pawn p)
    {
        if (!PawnCanOpen(p)) return;
        doorEmptyTicks = 0;
        RequestOpenTo(MaxSensorDoorOpenDistance());
    }

    public void NotifyApproachAndOpen(Pawn p, IntVec3 cell)
    {
        if (!PawnCanOpen(p)) return;
        doorEmptyTicks = 0;
        // 请求完整开门
        RequestOpenTo(MaxSensorDoorOpenDistance());
    }

    // 触发让路滑动
    private bool OpenDoor()
    {
        if (disabled) return false;
        return RequestOpenToInternal(MaxSensorDoorOpenDistance());
    }

    // 关回原位
    private bool CloseDoor()
    {
        if (disabled) return false;
        if (sliding || CurrentSensorDoorOpenDistance() == 0) return false;
        IntVec3 dir = SensorDoorOpenDir();
        if (dir == IntVec3.Zero) return false;
        sensorDoorTargetOpen = 0;
        slideDurationOverride = Props.sensorDoorOpenTicks;
        RefreshDoorPathCost();
        // 等待归位关门
        // 保持重试状态
        return TryMoveInternal(-dir, true, CurrentSensorDoorOpenDistance());
    }

    // 手动开关感应门
    public void ToggleDoorManual()
    {
        TryToggleDoorManual();
    }

    // 尝试手动开关门
    private bool TryToggleDoorManual()
    {
        if (disabled || sliding) return false;
        return doorOpen ? CloseDoor() : OpenDoor();
    }

    // 当前格内滑动偏移
    public Vector3 SlideDrawOffset => sliding ? slideDir.ToVector3() * slideProgress : Vector3.zero;

    // 解析可移动方向
    private IEnumerable<IntVec3> ResolveDirections()
    {
        return ResolveDirections(Props, parent.Rotation);
    }

    // 解析静态方向
    public static IEnumerable<IntVec3> ResolveDirections(CompProperties_BuildingMover props, Rot4 rot)
    {
        return ResolveDirections(props, rot, props.axis);
    }

    // 按轴解析方向
    public static IEnumerable<IntVec3> ResolveDirections(CompProperties_BuildingMover props, Rot4 rot, BuildingMoveAxis axis)
    {
        switch (axis)
        {
            case BuildingMoveAxis.MapEastWest:
                yield return IntVec3.East;
                yield return IntVec3.West;
                break;
            case BuildingMoveAxis.Custom:
                yield return props.customDirection;
                yield return -props.customDirection;
                break;
            case BuildingMoveAxis.FacingFourWay:
                IntVec3 fwd = rot.FacingCell;
                yield return fwd;
                yield return -fwd;
                yield return fwd.RotatedBy(RotationDirection.Clockwise);
                yield return fwd.RotatedBy(RotationDirection.Counterclockwise);
                break;
            default:
                IntVec3 right = IntVec3.East.RotatedBy(rot);
                yield return right;
                yield return -right;
                break;
        }
    }

    // 解析相对方向
    public static IntVec3 ResolveRelativeDir(BuildingRelativeDir dir, Rot4 rot, IntVec3 customDir, IntVec3 moveReverse)
    {
        switch (dir)
        {
            case BuildingRelativeDir.Forward: return rot.FacingCell;
            case BuildingRelativeDir.Backward: return -rot.FacingCell;
            case BuildingRelativeDir.Left: return rot.FacingCell.RotatedBy(RotationDirection.Counterclockwise);
            case BuildingRelativeDir.Right: return rot.FacingCell.RotatedBy(RotationDirection.Clockwise);
            case BuildingRelativeDir.Custom: return customDir;
            default: return moveReverse;
        }
    }

    // 检测目标占位
    private bool CanMoveTo(IntVec3 dir, int dist)
    {
        if (parent.Map == null) return false;
        CellRect rect = parent.OccupiedRect().MovedBy(dir * dist);
        if (!rect.InBounds(parent.Map)) return false;

        CellRect self = parent.OccupiedRect();
        foreach (IntVec3 c in rect)
        {
            if (self.Contains(c)) continue;
            if (!CellFreeForParent(c)) return false;
        }
        return true;
    }

    // 单格是否可容纳本建筑
    private bool CellFreeForParent(IntVec3 c, bool forCrush = false)
    {
        List<Thing> list = c.GetThingList(parent.Map);
        for (int i = 0; i < list.Count; i++)
        {
            Thing t = list[i];
            if (t == parent) continue;
            if (t is Pawn && parent.def.passability == Traversability.Impassable) return false;
            if (t.def.category != ThingCategory.Building && t.def.category != ThingCategory.Item) continue;
            // 检测残留阻挡
            if (forCrush) return false;
            if (GenSpawn.SpawningWipes(parent.def, t.def)) return false;
        }
        return true;
    }

    // 计算可移动格数
    private int MaxMovableDistance(IntVec3 dir, int wanted)
    {
        int ok = 0;
        for (int d = 1; d <= wanted; d++)
        {
            if (!CanMoveTo(dir, d)) break;
            ok = d;
        }
        return ok;
    }

    // 执行移动入口
    public void TryMove(IntVec3 dir)
    {
        TryMoveInternal(dir, Props.crushOutbound, WantedDistance());
    }

    // 指定距离移动
    public void TryMove(IntVec3 dir, int distance)
    {
        TryMoveInternal(dir, Props.crushOutbound, distance);
    }

    // 执行移动核心
    private bool TryMoveInternal(IntVec3 dir, bool crush, int wanted)
    {
        if (disabled || sliding || parent.Map == null) return false;
        int dist = crush ? MaxDistanceInBounds(dir, wanted) : MaxMovableDistance(dir, wanted);
        if (dist <= 0) return false;

        slideCrush = crush;
        slideDir = dir;

        return Props.moveMode == BuildingMoveMode.Teleport
            ? ExecuteTeleport(dir, dist, crush)
            : BeginSlide(dist);
    }

    // 执行瞬移推进
    private bool ExecuteTeleport(IntVec3 dir, int dist, bool crush)
    {
        bool moved = false;
        for (int d = 1; d <= dist; d++)
        {
            if (crush && !CrushCellsForMove(dir)) break;
            Relocate(dir);
            trackOffset += DirSign(dir);
            moved = true;
        }
        if (!moved) return false;
        CompleteMoveAction();
        return true;
    }

    // 启动滑动动画
    private bool BeginSlide(int dist)
    {
        slidePending = dist;
        slideTotalCells = dist;
        slideTicksElapsed = 0;
        slideProgress = 0f;
        sliding = true;
        parent.DirtyMapMesh(parent.Map);
        DirtyDamageLayer();
        return true;
    }

    // 刷新损坏覆盖层网格
    private void DirtyDamageLayer()
    {
        if (parent.Map == null) return;
        foreach (IntVec3 c in parent.OccupiedRect())
            parent.Map.mapDrawer.MapMeshDirty(c, MapMeshFlagDefOf.BuildingsDamage);
    }

    // 开关轨道往复触发
    private bool TryToggleTrack()
    {
        if (disabled || sliding || parent.Map == null) return false;

        List<IntVec3> dirs = new List<IntVec3>(ResolveDirections());
        if (dirs.Count == 0) return false;
        IntVec3 fwd = dirs[0];

        bool outbound = trackOffset == 0;
        IntVec3 dir = outbound ? fwd : -fwd;
        int wanted = outbound ? Props.moveDistance : Mathf.Abs(trackOffset);
        bool crush = outbound ? Props.crushOutbound : Props.crushReturn;
        return TryMoveInternal(dir, crush, wanted);
    }

    // 计算边界距离
    private int MaxDistanceInBounds(IntVec3 dir, int wanted)
    {
        int ok = 0;
        for (int d = 1; d <= wanted; d++)
        {
            CellRect rect = parent.OccupiedRect().MovedBy(dir * d);
            if (!rect.InBounds(parent.Map)) break;
            ok = d;
        }
        return ok;
    }

    // 本次期望移动格数
    private int WantedDistance()
    {
        if (Props.moveMode == BuildingMoveMode.SlideToEnd)
        {
            IntVec3 s = parent.Map.Size;
            return Mathf.Max(s.x, s.z);
        }
        return Props.moveDistance;
    }

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        if (disabled) return;
        if (Props.sensorDoor)
        {
            // 初始化门寻路
            EnsureSensorDoorAnchor();
            RegisterSensorDoorRuntime();
            RefreshDoorPathCost();
        }
    }

    // 刷新门寻路
    private void RefreshDoorPathCost()
    {
        if (parent.Map == null) return;
        if (Props.sensorDoor && !sliding)
        {
            foreach (IntVec3 c in SensorDoorFootprint().ClipInsideMap(parent.Map))
                parent.Map.regionDirtyer.Notify_WalkabilityChanged(c, true);
        }
        parent.Map.pathing.RecalculatePerceivedPathCostUnderThing(parent);
        if (Props.sensorDoor)
        {
            foreach (IntVec3 c in SensorDoorFootprint().ClipInsideMap(parent.Map))
                parent.Map.pathing.RecalculatePerceivedPathCostAt(c);
        }
        parent.Map.reachability.ClearCache();
    }

    // 注册感应门运行态
    private void RegisterSensorDoorRuntime()
    {
        if (sensorDoorRegistered) return;
        FFF_SensorDoorPatchManager.NotifySpawned();
        RegisterSensorDoor(this);
        sensorDoorRegistered = true;
    }

    // 注销感应门运行态
    private void DeregisterSensorDoorRuntime()
    {
        DeregisterSensorDoor(this);
        if (!sensorDoorRegistered) return;
        FFF_SensorDoorPatchManager.NotifyDespawned();
        sensorDoorRegistered = false;
    }

    public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
    {
        base.PostDeSpawn(map, mode);
        if (Props.sensorDoor)
        {
            DeregisterSensorDoorRuntime();
            // 重置门洞锚点
            sensorDoorAnchorSet = false;
        }
    }

    public override void CompTick()
    {
        base.CompTick();
        if (disabled) return;
        if (parent.Map == null || !parent.Spawned) return;

        if (sliding) { TickSlide(); return; }
        if (Props.sensorDoor) { TickSensorDoor(); return; }
        if (Props.allowProximity) TickProximity();
    }

    // 感应门自动关闭检测
    private void TickSensorDoor()
    {
        // 同步门洞锚点
        SyncSensorDoorAnchorIfMoved();
        if (!doorOpen || !Props.sensorDoorAutoClose) return;
        if (!parent.IsHashIntervalTick(15)) return;

        if (AnyPawnInDoorway()) { doorEmptyTicks = 0; return; }
        doorEmptyTicks += 15;
        if (doorEmptyTicks >= Props.sensorDoorCloseDelay) CloseDoor();
    }

    // 检测门洞小人
    private bool AnyPawnInDoorway()
    {
        CellRect rect = Props.sensorDoor ? SensorDoorFootprint() : parent.OccupiedRect();
        foreach (IntVec3 c in rect)
        {
            if (!c.InBounds(parent.Map)) continue;
            if (c.GetFirstPawn(parent.Map) != null) return true;
        }
        return false;
    }

    // 获取方向符号
    private int DirSign(IntVec3 dir)
    {
        List<IntVec3> dirs = Props.sensorDoor
            ? new List<IntVec3>(ResolveDirections(Props, parent.Rotation, Props.sensorDoorAxis))
            : new List<IntVec3>(ResolveDirections());
        if (dirs.Count > 0 && dir == -dirs[0]) return -1;
        return 1;
    }

    // 靠近检测
    private void TickProximity()
    {
        int interval = Props.proximityCheckInterval > 0 ? Props.proximityCheckInterval : 30;
        if (!parent.IsHashIntervalTick(interval)) return;
        if (!AnyTriggerPawnNear()) return;

        if (Props.trackToggle) { TryToggleTrack(); return; }

        foreach (IntVec3 dir in ResolveDirections())
        {
            if (MaxMovableDistance(dir, WantedDistance()) > 0) { TryMove(dir); return; }
        }
    }

    // 检测触发小人
    private bool AnyTriggerPawnNear()
    {
        foreach (Thing t in GenRadial.RadialDistinctThingsAround(parent.Position, parent.Map, Props.proximityRadius, true))
        {
            if (!(t is Pawn p) || p.Dead) continue;
            if (Props.proximityOnlyHumanlike && !p.RaceProps.Humanlike) continue;
            if (Props.proximityOnlyHostile && !p.HostileTo(parent.Faction)) continue;
            return true;
        }
        return false;
    }

    // 滑动时由本组件自绘
    public override bool DontDrawParent() => sliding;

    public override void PostDraw()
    {
        base.PostDraw();
        if (!sliding) return;
        Vector3 loc = parent.DrawPos + SlideDrawOffset;
        parent.Graphic.Draw(loc, parent.Rotation, parent);
        // 损坏覆盖层跟随
        if (parent is Building b) DamageOverlayRenderer.DrawShifted(b, SlideDrawOffset);
    }

    // 顺时针旋转建筑
    public void TryRotate()
    {
        TryRotateInternal();
    }

    // 尝试旋转建筑
    private bool TryRotateInternal()
    {
        if (disabled) return false;
        if (sliding || parent.Map == null) return false;
        Rot4 next = parent.Rotation.Rotated(RotationDirection.Clockwise);
        if (!CanOccupyWithRot(parent.Position, next)) return false;

        Map map = parent.Map;
        IntVec3 pos = parent.Position;
        parent.DeSpawn(DestroyMode.WillReplace);
        GenSpawn.Spawn(parent, pos, map, next, WipeMode.Vanish);
        Props.moveSound?.PlayOneShot(SoundInfo.InMap(parent));
        return true;
    }

    // 检测旋转占位
    private bool CanOccupyWithRot(IntVec3 center, Rot4 rot)
    {
        CellRect rect = GenAdj.OccupiedRect(center, rot, parent.def.Size);
        if (!rect.InBounds(parent.Map)) return false;

        CellRect self = parent.OccupiedRect();
        foreach (IntVec3 c in rect)
        {
            if (self.Contains(c)) continue;
            if (!CellFreeForParent(c)) return false;
        }
        return true;
    }

    [SyncMethod]
    private static void SyncedMove(CompBuildingMover comp, IntVec3 dir)
    {
        comp?.TryMove(dir);
    }

    [SyncMethod]
    private static void SyncedRotate(CompBuildingMover comp)
    {
        comp?.TryRotate();
    }

    [SyncMethod]
    private static void SyncedToggle(CompBuildingMover comp)
    {
        comp?.TryToggleTrack();
    }

    [SyncMethod]
    private static void SyncedDoorToggle(CompBuildingMover comp)
    {
        comp?.ToggleDoorManual();
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref sliding, "sliding", false);
        Scribe_Values.Look(ref slideProgress, "slideProgress", 0f);
        Scribe_Values.Look(ref slidePending, "slidePending", 0);
        Scribe_Values.Look(ref slideDir, "slideDir");
        Scribe_Values.Look(ref trackOffset, "trackOffset", 0);
        Scribe_Values.Look(ref slideCrush, "slideCrush", false);
        Scribe_Values.Look(ref slideTotalCells, "slideTotalCells", 0);
        Scribe_Values.Look(ref slideTicksElapsed, "slideTicksElapsed", 0);
        Scribe_Values.Look(ref doorOpen, "doorOpen", false);
        Scribe_Values.Look(ref doorEmptyTicks, "doorEmptyTicks", 0);
        Scribe_Values.Look(ref sensorDoorTargetOpen, "sensorDoorTargetOpen", 0);
        Scribe_Values.Look(ref pendingSignalActionIndex, "pendingSignalActionIndex", -1);
        Scribe_Values.Look(ref disabled, "buildingMoverDisabled", false);
        Scribe_Values.Look(ref pendingDisableAfterMove, "pendingDisableAfterMove", false);
        Scribe_Values.Look(ref sensorDoorClosedCenter, "sensorDoorClosedCenter");
        Scribe_Values.Look(ref sensorDoorAnchorSet, "sensorDoorAnchorSet", false);
    }
}
