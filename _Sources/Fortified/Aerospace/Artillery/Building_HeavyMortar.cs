using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 重型迫擊砲：原版 <see cref="Building_TurretGun"/>（砲手裝填、可換砲彈）＋世界砲擊
    /// （<see cref="CompArtilleryStrike"/>）。
    ///
    /// 原版砲塔邏輯與世界砲擊共用同一根砲管、同一發載入的砲彈，所以兩者同時只能有一個在進行
    /// （同 <see cref="Building_LongRangeGun"/>）：
    ///   - 世界砲擊進行中不自動接戰本地圖目標（<see cref="TryFindNewTarget"/> 回無效），也不能指定本地目標
    ///     （<see cref="CanSetForcedTarget"/> 為 false，原版 Tick 會解除既有的強制目標）；
    ///     已在預熱的本地射擊會被放掉（正在出膛的這一發不打斷）。
    ///   - 下世界砲擊指令即由世界砲擊接手；要回到本地射擊，先停止世界砲擊。
    ///   - 原版 <see cref="Building_TurretGun.Tick"/> 先呼叫 base.Tick()（comp 在這裡 tick，
    ///     世界瞄準方位也在此寫入），之後才跑砲塔自己的 TurretTopTick；所以每次 Tick 結束後
    ///     再把砲管拉回世界瞄準方位。
    /// 沒有世界目標時完全交給原版砲塔邏輯。
    /// </summary>
    public class Building_HeavyMortar : Building_TurretGun
    {
        private CompArtilleryStrike artilleryInt;
        private bool artilleryCached;

        private CompArtilleryStrike Artillery
        {
            get
            {
                if (!artilleryCached) { artilleryInt = GetComp<CompArtilleryStrike>(); artilleryCached = true; }
                return artilleryInt;
            }
        }

        /// <summary>世界砲擊正在持續射擊：砲管被占用，本地圖射擊暫停。</summary>
        public bool WorldFiring => Artillery != null && Artillery.IsSustainedFiring;

        /// <summary>世界砲擊進行中不能指定本地目標。</summary>
        protected override bool CanSetForcedTarget => base.CanSetForcedTarget && !WorldFiring;

        /// <summary>世界砲擊進行中不自動接戰本地目標。</summary>
        public override LocalTargetInfo TryFindNewTarget()
            => WorldFiring ? LocalTargetInfo.Invalid : base.TryFindNewTarget();

        protected override void Tick()
        {
            // 世界砲擊接手砲管：放掉本地目標與預熱（已在射擊的這一發不打斷）。
            if (WorldFiring && currentTargetInt.IsValid && AttackVerb.state != VerbState.Bursting)
            {
                currentTargetInt = LocalTargetInfo.Invalid;
                burstWarmupTicksLeft = 0;
            }

            base.Tick();

            if (!WorldFiring) { return; }
            float aim = Artillery.AimAngle;
            if (top != null && !float.IsNaN(aim)) { top.CurRotation = aim; }
        }
    }
}
