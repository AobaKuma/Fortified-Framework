using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Fortified
{
    public class Projectile_ClusterBomb : Projectile
    {
        private const int DefaultDismantlTickThreshold = 10;
        private const int ImpactAnticipateTicksThreshold = 60;
        private const int GoodwillPenalty = 10;

        private int tickCounter = 0;
        private int dismantleDistance = 0;

        ModExtension_ClusterBomb Extension => def.GetModExtension<ModExtension_ClusterBomb>();

        protected override void Tick()
        {
            if (landed)
            {
                return;
            }

            ticksToImpact--;

            if (!ExactPosition.InBounds(base.Map))
            {
                ticksToImpact++;
                base.Position = ExactPosition.ToIntVec3();
                Destroy();
                return;
            }

            PlayImpactAnticipateSound();

            if (ticksToImpact <= 0)
            {
                if (base.DestinationCell.InBounds(base.Map))
                {
                    base.Position = base.DestinationCell;
                }
                Destroy();
                return;
            }

            tickCounter++;

            CheckAndExecuteDismantle();
        }

        private void PlayImpactAnticipateSound()
        {
            if (ticksToImpact == ImpactAnticipateTicksThreshold && 
                Find.TickManager.CurTimeSpeed == TimeSpeed.Normal && 
                def.projectile.soundImpactAnticipate != null)
            {
                def.projectile.soundImpactAnticipate.PlayOneShot(this);
            }
        }

        private void CheckAndExecuteDismantle()
        {
            if (Vector2.Distance(origin, ExactPosition) <= Extension.safetyDistance)
            {
                return;
            }

            IntRange dismantleDistanceRange = Extension.TDDistance;
            bool shouldDismantle = IsDismantleConditionMet(dismantleDistanceRange);

            if (shouldDismantle)
            {
                ExecuteDismantle();
            }
        }

        private bool IsDismantleConditionMet(IntRange dismantleDistanceRange)
        {
            if (dismantleDistanceRange.min == 0 && dismantleDistanceRange.max == 0)
            {
                return CheckDefaultDismantle();
            }

            return CheckDistanceBasedDismantle(dismantleDistanceRange.min, dismantleDistanceRange.max);
        }

        private bool CheckDefaultDismantle()
        {
            Vector3 launcherPos = launcher.Position.ToVector3();
            Vector3 targetPos = base.DestinationCell.ToVector3();
            int totalDistance = CalculateDistance(launcherPos, targetPos);

            if (dismantleDistance == 0)
            {
                dismantleDistance = totalDistance / 2;
            }

            Vector3 currentPos = ExactPosition;
            int distanceFromLauncher = CalculateDistance(currentPos, launcherPos);

            if (tickCounter >= DefaultDismantlTickThreshold && distanceFromLauncher >= dismantleDistance)
            {
                tickCounter = 0;
                return true;
            }

            return false;
        }

        private bool CheckDistanceBasedDismantle(int minDistance, int maxDistance)
        {
            Vector3 currentPos = ExactPosition;
            Vector3 targetPos = base.DestinationCell.ToVector3();
            Vector3 launcherPos = launcher.Position.ToVector3();

            int distanceToTarget = CalculateDistance(currentPos, targetPos);
            int distanceFromLauncher = CalculateDistance(currentPos, launcherPos);

            if (distanceToTarget <= maxDistance && distanceFromLauncher >= minDistance)
            {
                tickCounter = 0;
                return true;
            }

            return false;
        }

        private void ExecuteDismantle()
        {
            if (Extension.doDismantlExplosion)
            {
                CreateDismantleExplosion();
            }
            SpawnClusterProjectiles();
        }

        private void CreateDismantleExplosion()
        {
            GenExplosion.DoExplosion(
                ExactPosition.ToIntVec3(),
                base.Map,
                Extension.dismantlExplosionRadius,
                Extension.dismantlExplosionDam,
                (Thing)this,
                Extension.dismantlExplosionDamAmount,
                Extension.dismantlExplosionArmorPenetration,
                Extension.dismantlExplosionSound,
                null, null, null, null,
                1f, 1, null, null, 0, false, null, 0f, 1, 0f, false, null, null);
        }

        private void SpawnClusterProjectiles()
        {
            ThingDef projectileDef = Extension.projectile;
            for (int i = 0; i < Extension.clusterBurstCount; i++)
            {
                SpawnSingleProjectile(projectileDef);
            }

            // 每發子母彈解體時只結算一次聲望（不是每顆子彈一次），
            // 而且要趕在 Destroy() 之前——Destroy 之後 Map 就沒了。
            ApplyGoodwillCorrection(GetAffectedCells());

            Destroy();
        }

        private void SpawnSingleProjectile(ThingDef projectileDef)
        {
            Projectile spawnedProjectile = (Projectile)GenSpawn.Spawn(projectileDef, ExactPosition.ToIntVec3(), base.Map);
            List<IntVec3> affectedCells = GetAffectedCells();
            IntVec3 targetCell = affectedCells.RandomElement();

            spawnedProjectile.Launch(this, ExactPosition, targetCell, targetCell, ProjectileHitFlags.All, 
                preventFriendlyFire: false, ThingMaker.MakeThing(base.EquipmentDef));
        }

        private List<IntVec3> GetAffectedCells()
        {
            float errorRange = Extension.forceMissingRange;
            return GenRadial.RadialCellsAround(base.DestinationCell, errorRange, useCenter: true).ToList();
        }

        /// <summary>
        /// 撒佈範圍內被波及的非敵對勢力扣好感。
        /// 只對「玩家發射」的子母彈生效：敵方打過來的彈不該動玩家與中立勢力的關係。
        /// 同一個勢力每次解體只扣一次（多個 pawn 不重複扣）。
        /// </summary>
        private void ApplyGoodwillCorrection(List<IntVec3> cells)
        {
            Faction attacker = launcher?.Faction;
            if (attacker == null || !attacker.IsPlayer)
            {
                return;
            }

            HashSet<Faction> corrected = new HashSet<Faction>();
            foreach (IntVec3 cell in cells)
            {
                List<Thing> thingList = cell.GetThingList(base.Map);
                foreach (Thing thing in thingList)
                {
                    if (!(thing is Pawn pawn)) { continue; }
                    if (pawn.Dead || pawn.Faction == null) { continue; }
                    if (!corrected.Add(pawn.Faction)) { continue; }

                    CorrectPawnGoodwill(pawn.Faction);
                }
            }
        }

        /// <summary>
        /// 對單一勢力扣固定好感。
        /// 舊版是「把關係壓到 -10」：
        /// <c>-GoodwillPenalty - baseGoodwill + GoodwillWith(faction)</c>，
        /// 對 +80 的勢力等於一次扣掉 90 點，明顯不是原意 → 改成純扣 <see cref="GoodwillPenalty"/>。
        /// </summary>
        private void CorrectPawnGoodwill(Faction faction)
        {
            if (faction == null || faction.IsPlayer)
            {
                return;
            }

            Faction attacker = launcher?.Faction;
            if (attacker == null || faction.HostileTo(attacker))
            {
                return;
            }

            faction.TryAffectGoodwillWith(attacker, -GoodwillPenalty);
        }

        private int CalculateDistance(Vector3 from, Vector3 to)
        {
            IntVec3 fromCell = from.ToIntVec3();
            IntVec3 toCell = to.ToIntVec3();
            
            int deltaX = toCell.x - fromCell.x;
            int deltaZ = toCell.z - fromCell.z;
            
            return Math.Abs((int)Math.Round(Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ)));
        }
    }
}
