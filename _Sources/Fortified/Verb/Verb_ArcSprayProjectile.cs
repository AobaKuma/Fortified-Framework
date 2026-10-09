using Verse;

namespace Fortified
{
    /// <summary>
    /// 原版弧形掃射 Verb。掃射路徑改為之字形來回掃後回到中軸點，並加上近距離保護，
    /// 詳見 ArcSprayPathUtility；路徑長度 = 連發數，sprayNumExtraCells 不再使用。
    /// </summary>
    public class Verb_ArcSprayProjectile : Verb_ArcSpray
    {
        // 左右邊緣之間來回掃的趟數 (不含出發與收尾那兩段)
        protected virtual int ZigzagSweeps => ArcSprayPathUtility.DefaultSweeps;

        // 掃射點相對砲口方向的最大左右夾角 (度)
        protected virtual float MaxSweepHalfAngle => ArcSprayPathUtility.DefaultMaxHalfAngle;

        // 掃射格離砲口中心的最小距離
        protected virtual float MinCellDistance => ArcSprayPathUtility.DefaultMinCellDistance(caster, verbProps);

        protected override void PreparePath()
        {
            ArcSprayPathUtility.BuildZigzagPath(path, caster, currentTarget, verbProps, ShotsPerBurst, ZigzagSweeps, MaxSweepHalfAngle, MinCellDistance);
        }

        protected override void HitCell(IntVec3 cell)
        {
            base.HitCell(cell);
            ((Projectile)GenSpawn.Spawn(verbProps.defaultProjectile, caster.Position, caster.Map, WipeMode.Vanish)).Launch(caster, caster.DrawPos, cell, cell, ProjectileHitFlags.All, false, null, null);
        }
    }
}
