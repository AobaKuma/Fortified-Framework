using CombatExtended;
using Fortified;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FortifiedCE
{
    /// <summary>
    /// CE 版武器用弧形掃射 Verb (對應原版 Fortified.Verb_ArcSprayProjectile)。
    /// 繼承 Verb_ShootCE，所以彈藥 (CompAmmoUser)、射擊模式 (CompFireModes)、後座力、
    /// 砲塔 (Building_TurretGunCE) 與 Pawn 持槍全部走 CE 原本的流程。
    ///
    /// 每一發都把 currentTarget 暫時換成掃射路徑上的格子再交給 Verb_ShootCE 開火：
    ///   - defaultProjectile / 彈藥投射物是 ProjectileCE → 走 CE 彈道 (含 CE 火焰噴射彈)
    ///   - 是原版 Projectile → 走原版 Projectile.Launch，仍會消耗 CE 彈藥
    /// sprayEffecterDef 每發都會從 caster 射向該格子，可用來畫火焰束 / 泡沫束之類的視覺效果。
    ///
    /// 路徑長度固定等於本次連發的實際射擊數 (ShotsPerBurst)，不再依賴 sprayNumExtraCells，
    /// 所以切換 CE 射擊模式 (單發 / 點放 / 全自動) 都不會超出範圍，而且整段路徑一定掃完。
    ///
    /// 掃射軌跡是之字形：從中軸點 (目標格) 出發，左右邊緣之間來回掃後回到中軸點收尾，
    /// 並有近距離保護 (不掃到砲塔旁邊或背後)，兩者與原版 Fortified.Verb_ArcSprayProjectile
    /// 共用 Fortified.ArcSprayPathUtility，細節見該類別。
    ///
    /// CE 的瞄準模式選「瞄準射擊」(AimMode.AimedShot) 時不掃射，完全回到 Verb_ShootCE 原本的行為：
    /// 對目標本身做單點精準射擊 (含瞄準延遲、鎖定方向累積後座力)。其餘模式 (快速射擊 / 壓制射擊) 才掃射。
    /// </summary>
    public class Verb_ArcSprayProjectileCE : Verb_ShootCE
    {
        protected List<IntVec3> path = new List<IntVec3>();
        protected Vector3 initialTargetPosition;

        // 左右邊緣之間來回掃的趟數 (不含出發與收尾那兩段)
        protected virtual int ZigzagSweeps => ArcSprayPathUtility.DefaultSweeps;

        // 掃射點相對砲口方向的最大左右夾角 (度)
        protected virtual float MaxSweepHalfAngle => ArcSprayPathUtility.DefaultMaxHalfAngle;

        // 掃射格離砲口中心的最小距離
        protected virtual float MinCellDistance => ArcSprayPathUtility.DefaultMinCellDistance(caster, verbProps);

        // 玩家 / AI 目前選的是「瞄準射擊」：此時不掃射，照 Verb_ShootCE 原本的方式射擊
        protected bool IsAimedShot => CompFireModes != null && CompFireModes.CurrentAimMode == AimMode.AimedShot;

        private int CurrentPathIndex => Mathf.Clamp(ShotsPerBurst - burstShotsLeft, 0, path.Count - 1);

        // CE 連發時 (numShotsFired > 0) 會把射擊方向與仰角鎖在第一發，讓後座力累積；
        // 掃射每一發的目標都不同，不關掉的話第 2 發起全部會沿第一發的方向射出，變成定點連射。
        // 沒有掃射路徑時 (瞄準射擊 / 讀檔中途) 維持 CE 原本的鎖定行為。
        protected override bool LockRotationAndAngle => path.Count == 0 && base.LockRotationAndAngle;

        // 砲塔頂 (TurretTop.DrawTurret) 與 Pawn 持槍角度都吃這個，讓槍口跟著掃射路徑轉
        public override float? AimAngleOverride
        {
            get
            {
                if (state == VerbState.Bursting && path.Count > 0)
                {
                    return (path[CurrentPathIndex].ToVector3Shifted() - caster.DrawPos).AngleFlat();
                }
                return null;
            }
        }

        public override void WarmupComplete()
        {
            // Verb_ShootCE.WarmupComplete 在瞄準模式下可能先延長暖機再回頭呼叫一次，
            // 兩次都重算路徑沒有副作用；真正開火前路徑一定已就緒。
            initialTargetPosition = currentTarget.CenterVector3;
            if (IsAimedShot)
            {
                // 路徑留空 → TryCastShot 走 base (Verb_ShootCE)、AimAngleOverride 回 null，槍口照常追目標
                path.Clear();
            }
            else
            {
                PreparePath();
            }
            base.WarmupComplete();
        }

        public override bool TryCastShot()
        {
            if (path.Count == 0)
            {
                // 沒有路徑 (例如讀檔中途) 就退化成普通射擊
                return base.TryCastShot();
            }
            if (currentTarget.HasThing && currentTarget.Thing.Map != caster.Map)
            {
                return false;
            }

            IntVec3 cell = path[CurrentPathIndex];
            LocalTargetInfo originalTarget = currentTarget;
            currentTarget = new LocalTargetInfo(cell);
            try
            {
                verbProps.sprayEffecterDef?.Spawn(caster.Position, cell, caster.Map);

                ThingDef projectileDef = Projectile;
                if (projectileDef == null)
                {
                    return false;
                }

                bool fired;
                if (typeof(ProjectileCE).IsAssignableFrom(projectileDef.thingClass))
                {
                    // CE 投射物：完整走 Verb_ShootCE → Verb_LaunchProjectileCE 的彈道與彈藥流程
                    fired = base.TryCastShot();
                }
                else
                {
                    fired = TryCastVanillaProjectile(projectileDef, cell);
                }

                if (!fired && CompAmmo != null && !CompAmmo.CanBeFiredNow)
                {
                    // 沒彈藥了才真的中斷連發
                    return false;
                }
                // 個別格子沒有射線 (被牆擋住) 就跳過那一格，繼續掃下一格
                lastShotTick = Find.TickManager.TicksGame;
                return true;
            }
            finally
            {
                currentTarget = originalTarget;
            }
        }

        // 原版投射物回退：不走 CE 彈道，但仍然消耗 CE 彈藥
        private bool TryCastVanillaProjectile(ThingDef projectileDef, IntVec3 cell)
        {
            if (CompAmmo != null && !CompAmmo.TryPrepareShot())
            {
                return false;
            }
            Projectile projectile = (Projectile)GenSpawn.Spawn(projectileDef, caster.Position, caster.Map);
            projectile.Launch(caster, caster.DrawPos, cell, cell, ProjectileHitFlags.IntendedTarget, preventFriendlyFire, EquipmentSource);
            numShotsFired++;

            if (CompAmmo == null)
            {
                return true;
            }
            int ammoPerShot = (CompAmmo.Props.ammoSet?.ammoConsumedPerShot ?? 1) * VerbPropsCE.ammoConsumedPerShotCount;
            CompAmmo.Notify_ShotFired(ammoPerShot);
            if (ShooterPawn != null && !CompAmmo.CanBeFiredNow)
            {
                CompAmmo.TryStartReload();
            }
            if (!CompAmmo.HasMagazine && CompAmmo.UseAmmo)
            {
                return CompAmmo.Notify_PostShotFired();
            }
            return true;
        }

        // 之字形掃射：路徑長度 = 本次連發射擊數，第一發與最後一發都落在中軸點 (目標格)；詳見 ArcSprayPathUtility
        protected virtual void PreparePath()
        {
            ArcSprayPathUtility.BuildZigzagPath(path, caster, currentTarget, verbProps, ShotsPerBurst, ZigzagSweeps, MaxSweepHalfAngle, MinCellDistance);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref path, "path", LookMode.Value);
            Scribe_Values.Look(ref initialTargetPosition, "initialTargetPosition");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && path == null)
            {
                path = new List<IntVec3>();
            }
        }
    }
}
