using System.Collections.Generic;
using AirEffectorShot = Fortified.AirEffectorShot;
using AirEngagement = Fortified.AirEngagement;
using IAirEffector = Fortified.IAirEffector;
using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>階段二：導彈發射裝置。受火控雷達指派，發射中過程攔截彈。</summary>
    public class CompProperties_MissileLauncher : CompProperties
    {
        /// <summary>
        /// 平台預設的攔截彈升空 def（thingClass 應為 <see cref="Projectile_MissileLaunch"/>）：裝填彈體沒有
        /// <see cref="MissileBodyDef.launchProjectile"/> 時使用。欄位名沿用舊稱（XML 相容），有設定才表示這座發射器能攔截（<see cref="CanIntercept"/>）。
        /// </summary>
        public ThingDef interceptorSkyfaller;

        /// <summary>這座發射器是否具備攔截能力（設定了攔截彈升空 def）。</summary>
        public bool CanIntercept => interceptorSkyfaller != null;

        /// <summary>兩次發射間的裝填冷卻（ticks）。</summary>
        public int reloadCooldownTicks = 180;

        /// <summary>攔截彈飛行時間（ticks）：占用火力通道直到結算。</summary>
        public int interceptorTravelTicks = 180;

        /// <summary>每個指定目標連射的攔截彈數；受實際裝填彈數限制。</summary>
        public int interceptorSalvoSize = 1;

        public CompProperties_MissileLauncher()
        {
            compClass = typeof(CompMissileLauncher_Interceptor);
        }

        public override System.Collections.Generic.IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef)) { yield return e; }
            string launchError = MissileProjectileMaker.LaunchDefError(interceptorSkyfaller, "interceptorSkyfaller");
            if (launchError != null) { yield return launchError; }
        }
    }

    /// <summary>
    /// 防空攔截彈旋轉發射架：<see cref="CompMissileLauncher"/> 家族中負責地圖內超視距攔截的成員。
    /// 與 <see cref="CompMissileLauncher_Ballistic"/>／<see cref="CompMissileLauncher_Rail"/> 不同，
    /// 世界航跡由 WorldComponent_AirDefenseNetwork 呼叫 FireWorldInterceptor：消耗實體彈、生成純視覺升空彈，
    /// 世界物件接手會合結算；空投與舊波次仍由 MapComponent_AirDefense 呼叫 FireInterceptor。
    /// </summary>
    public class CompMissileLauncher_Interceptor : CompMissileLauncher, IAirEffector, IDomainAwareEffector
    {
        // ---- IAirEffector：世界層接戰 ----
        public Thing Source => parent;
        public bool UsesFireControl => true;
        /// <summary>玩家對這座建築的砲塔按了停火：攔截彈同樣不發射（同末端 CIWS，見 <see cref="TurretHoldFireUtility"/>）。</summary>
        public bool Armed => Props.CanIntercept && Active && !TurretHoldFireUtility.HoldingFire(parent);
        public int ReadyTick => CooldownUntil;
        public bool CanFire(int now) => ReadyToFire(now);
        /// <summary>只打和裝填中的攔截彈同一領域的目標（<see cref="MissileBodyDef.domain"/>）。</summary>
        public bool CanEngage(AirEngagement engagement) => engagement == null || Engages(engagement.Domain);

        public bool Engages(CombatDomainDef domain) => CombatDomainUtility.Resolve(GetLoadedMissileConfig()?.body?.domain) == CombatDomainUtility.Resolve(domain);
        public AirEffectorShot PrepareShot(AirEngagement engagement) => AirDefenseHooks.ShotFrom(GetLoadedMissileConfig());
        public bool TryFire(AirEngagement engagement, int now, Vector3 meetingPoint)
            => parent.Map != null && FireWorldInterceptor(now, MissileProjectileMaker.WorldInterceptBearing(parent.Map.Tile, meetingPoint));

        public CompProperties_MissileLauncher Props => (CompProperties_MissileLauncher)props;

        protected override int LaunchCooldownTicks => Props.reloadCooldownTicks;

        protected override string CooldownScribeKey => "cooldownUntil";

        protected override string CooldownTranslationKey => "FFF.MissileLauncher.Interceptor.Reloading";

        // 彈藥來源：parent 為 Building_MissileRack（容器），攔截彈以實體 Thing 逐枚存放，
        // 或是由 CompMissileRackStorage 掛在砲塔型平台上（野戰防空砲）。
        private ThingOwner HeldContainer =>
            parent.GetComp<CompMissileRackStorage>()?.HeldContainer
            ?? (parent as IThingHolder)?.GetDirectlyHeldThings();

        // 輕型發射箱與對地發射軌共用彈艙：只取攔截／兩用彈（對地彈不能當攔截彈射掉），
        // 且被打擊任務保留的導彈（StrikeReservation）不取用。
        private Thing LoadedMissile
        {
            get
            {
                ThingOwner owner = HeldContainer;
                if (owner == null || owner.Count == 0) { return null; }
                MapComponent_StrikePlanner planner = StrikeReservation.PlannerFor(parent);
                for (int i = 0; i < owner.Count; i++)
                {
                    if (!MissileRoleExtension.CanIntercept(owner[i])) { continue; }
                    if (planner == null || !planner.IsReserved(owner[i])) { return owner[i]; }
                }
                return null;
            }
        }

        public override bool HasAmmo => LoadedMissile != null;

        /// <summary>
        /// 取得已裝填攔截彈的 MissileConfig，供 BVR 系統讀取戰鬥部加成與彈體攔截係數。
        /// 物品的 CompMissileConfig 優先，其次是 <see cref="MissileItemExtension"/> 宣告的彈體；都沒有回傳 null。
        /// </summary>
        public MissileConfig GetLoadedMissileConfig()
        {
            return MissileItemExtension.ConfigFor(LoadedMissile);
        }

        // 便宜的條件在前；Active 已包含 HasAmmo（找彈要掃容器與打擊保留），不再重算一次。
        public bool ReadyToFire(int now)
            => Props.CanIntercept && !OnCooldown(now) && Active && !TurretHoldFireUtility.HoldingFire(parent);

        /// <summary>
        /// 發射一枚攔截彈。<paramref name="heading"/> 為來襲方位（升空後轉向飛出地圖）；不給時以
        /// <see cref="MissileProjectileMaker.IncomingBearing"/> 由目標推算。經發射排程錯開生成。
        /// </summary>
        public void FireInterceptor(AirDefenseTarget target, float hitChance, int now, float? heading = null)
        {
            FireInterceptorRounds(target, hitChance, now, heading, 1, out _);
        }

        private int FireInterceptorRounds(AirDefenseTarget target, float hitChance, int now, float? heading, int requestedRounds,
            out int lastSpawnTick)
        {
            lastSpawnTick = now;
            if (!Props.CanIntercept || parent.Map == null) { return 0; }

            // 初始升空角沿用建築朝向；具旋轉砲頭的發射架採用砲頭角度。heading 留給升空後的攔截航線。
            heading ??= MissileProjectileMaker.IncomingBearing(target, parent.Position);
            float launchAngle = parent.TryGetComp<CompTurretFacingRenderer>()?.CurRotation ?? MissileRackUtility.LaunchRotationOf(parent).AsAngle;
            CompTurretFacingRenderer facing = parent.TryGetComp<CompTurretFacingRenderer>();
            MapComponent_StrikePlanner planner = StrikeReservation.PlannerFor(parent);
            ThingOwner owner = HeldContainer;
            List<(Thing missile, ThingDef launchDef, Vector3 launchOffset, bool hasLaunchOffset)> rounds
                = new List<(Thing, ThingDef, Vector3, bool)>();
            int requested = Mathf.Max(1, requestedRounds);
            if (owner != null)
            {
                for (int i = 0; i < owner.Count && rounds.Count < requested; i++)
                {
                    Thing missile = owner[i];
                    if (!MissileRoleExtension.CanIntercept(missile)) { continue; }
                    if (planner != null && planner.IsReserved(missile)) { continue; }
                    Vector3 launchOffset = Vector3.zero;
                    bool hasLaunchOffset = facing != null && facing.TryGetRenderedAmmoOffset(missile, out launchOffset);
                    if (!hasLaunchOffset)
                    {
                        CompMissileRackRenderer rackRenderer = parent.TryGetComp<CompMissileRackRenderer>();
                        hasLaunchOffset = rackRenderer != null && rackRenderer.TryGetRenderedAmmoOffset(missile, out launchOffset);
                    }
                    ThingDef roundLaunchDef = MissileItemExtension.BodyOf(missile)?.launchProjectile ?? Props.interceptorSkyfaller;
                    // 堆疊模組下一個 Thing 可能是多枚，每枚各算一發（發射時 ConsumeOne 逐枚扣除）。
                    for (int n = 0; n < missile.stackCount && rounds.Count < requested; n++)
                    {
                        rounds.Add((missile, roundLaunchDef, launchOffset, hasLaunchOffset));
                    }
                }
            }

            int fired = 0;
            foreach (var round in rounds)
            {
                int spawnTick = MapComponent_MissileLaunchQueue.Launch(parent.Map, new PendingMissileLaunch
                {
                    def = round.launchDef,
                    cell = parent.Position,
                    rotation = MissileRackUtility.LaunchRotationOf(parent),
                    hasLaunchAngle = facing != null,
                    launchAngle = launchAngle,
                    hasLaunchOffset = round.hasLaunchOffset,
                    launchOffset = round.launchOffset,
                    launcher = parent,
                    interceptor = true,
                    targetId = target.id,
                    hitChance = hitChance,
                    hasHeading = heading != null,
                    heading = heading ?? 0f,
                });
                lastSpawnTick = Mathf.Max(lastSpawnTick, spawnTick);
                MissileRackUtility.ConsumeOne(round.missile);
                fired++;
            }

            if (fired > 0)
            {
                StartCooldown(now);
                if (target != null && target.id >= 0) { target.interceptorsInFlight += fired; }
            }
            return fired;

        }

        // 攔截的人工瞄準追蹤（不存檔：讀檔後重新瞄準）。
        private const int AimTrackGapTicks = 5;
        private int aimTargetId = int.MinValue;
        private int aimSinceTick;
        private int aimLastTick = -100000;

        /// <summary>只回報攔截的追蹤瞄準；對地發射的排程瞄準由同一座建築的對地發射 comp 顯示，避免重複一行。</summary>
        public override int AimingTicksLeft(int now)
        {
            int left = 0;
            int aim = MissileLauncherExtension.AimTicksOf(parent);
            if (aim > 0 && now - aimLastTick <= AimTrackGapTicks)
            {
                left = Mathf.Max(left, aim - (now - aimSinceTick));
            }
            return left;
        }

        /// <summary>
        /// 試射地圖目標。旋轉發射架先對準本次指派目標，砲頭進入容許角度後才發射；
        /// 避免渲染器追蹤另一個更急迫的波次時，導彈沿著錯的砲頭角度離架。
        /// </summary>
        public bool TryFireInterceptor(AirDefenseTarget target, float hitChance, int now, out int roundsFired, out int lastSpawnTick)
        {
            roundsFired = 0;
            lastSpawnTick = now;
            CompTurretFacingRenderer facing = parent.TryGetComp<CompTurretFacingRenderer>();
            float? heading = MissileProjectileMaker.IncomingBearing(target, parent.Position);
            if (facing != null && heading.HasValue)
            {
                Vector3 aimPoint = parent.DrawPos + MissileMapEntryUtility.HeadingVector(heading.Value) * 10f;
                facing.ForceFaceTarget(aimPoint, 2);
                float tolerance = Mathf.Max(1f, facing.Props.rotationSpeedDegPerTick * 0.5f);
                if (!facing.IsFacing(aimPoint, tolerance)) { return false; }
            }

            // 人工瞄準（MissileLauncherExtension.aimTicks）：對準後還要持續追蹤同一個目標滿這段時間；
            // 換目標或中斷追蹤（火控不再逐 tick 重試）就重新計時。
            int aim = MissileLauncherExtension.AimTicksOf(parent);
            if (aim > 0)
            {
                if (target.id != aimTargetId || now - aimLastTick > AimTrackGapTicks)
                {
                    aimTargetId = target.id;
                    aimSinceTick = now;
                }
                aimLastTick = now;
                if (now - aimSinceTick < aim) { return false; }
            }

            roundsFired = FireInterceptorRounds(target, hitChance, now, heading, Props.interceptorSalvoSize, out lastSpawnTick);
            return roundsFired > 0;
        }

        /// <summary>
        /// Consume a real missile and launch a visual-only interceptor; world scheduling owns the roll.
        /// <paramref name="heading"/>: bearing toward the world meeting point, so the missile turns that way after the ascent.
        /// </summary>
        public bool FireWorldInterceptor(int now, float? heading = null)
        {
            if (!ReadyToFire(now) || parent.Map == null) return false;
            FireInterceptor(new AirDefenseTarget { id = -1 }, 0f, now, heading);
            return true;
        }

        protected override string LoadedStatusText => "FFF.MissileLauncher.Interceptor.Ready".Translate();

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            MapComponent_AirDefense m = Manager;
            if (m != null) { m.launchers.Add(this); }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            MapComponent_AirDefense m = map != null ? map.GetComponent<MapComponent_AirDefense>() : null;
            if (m != null) { m.launchers.Remove(this); }
        }
    }
}
