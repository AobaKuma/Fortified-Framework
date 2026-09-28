using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Fortified
{
    // ════════════════════════════════════════════════════════════
    //  CompProperties_AlertEffector  (基類 Props)
    // ════════════════════════════════════════════════════════════
    /// <summary>
    /// 所有警報效果 Comp 的 CompProperties 基類。
    /// </summary>
    public class CompProperties_AlertEffector : CompProperties
    {
        /// <summary>監聽的 Signal 字串，需與 CompAlertScanner.signalString 一致。</summary>
        public string listenSignal = "FFF_AlertScanner_Triggered";

        /// <summary>每次收到警報時實際觸發的機率（0~1）。</summary>
        public float triggerChance = 1f;

        /// <summary>是否只觸發一次（true = 一次性，false = 可重複觸發）。</summary>
        public bool oneShot = false;

        /// <summary>
        /// 建築整個還在戰爭迷霧裡（玩家還沒發現）時不觸發。適合只作用在自身周圍的反制建築（毒氣口、聲波塔），
        /// 免得在沒人看得到的地方空放、白白耗掉存量或 oneShot。
        /// Don't fire while the whole building is still fogged (undiscovered). Suits counter-measures that only act
        /// around themselves (gas vents, emitters), so they don't go off unseen and waste charges or their oneShot.
        /// </summary>
        public bool inactiveWhenFogged = false;

        public CompProperties_AlertEffector()
        {
            compClass = typeof(CompAlertEffector);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  CompAlertEffector  (基類)
    // ════════════════════════════════════════════════════════════
    /// <summary>
    /// 警報效果基類。
    /// 接收 MapComponent_AlertCounter 的警報通知（透過 Signal 或直接呼叫 <see cref="OnAlertNotify"/>）。
    /// 子類覆寫 <see cref="DoEffect"/> 實作具體效果。
    /// </summary>
    public class CompAlertEffector : ThingComp
    {
        private bool hasFired = false;

        /// <summary>
        /// 上次實際觸發 DoEffect() 的 tick。
        /// 用於去重：同一 tick 內無論收到幾個 Signal 都只觸發一次。
        /// （多個 CompAlertScanner 同 tick 偵測到威脅時會各自 SendSignal，
        ///   此欄位確保同一 Effector 建築不會在同 tick 重複釋放。）
        /// 不需持久化——讀檔後 tick 必然不同，不影響正確性。
        /// </summary>
        private int lastFiredTick = -1;

        public CompProperties_AlertEffector Props => (CompProperties_AlertEffector)props;

        // ── Signal 接收 ──────────────────────────────────────────
        public override void Notify_SignalReceived(Signal signal)
        {
            base.Notify_SignalReceived(signal);
            // Signal 是全域的：只理會同一張地圖的警報，地下口袋地圖的掃描器不該觸發地表的效果器。
            // Signals are global: only honour alarms from this map, so a pocket-map scanner can't set off surface effectors.
            if (signal.tag == Props.listenSignal && signal.IsForParent(parent))
                OnAlertNotify(signal);
        }

        /// <summary>
        /// 由外部（MapComponent_AlertCounter 或 Signal）呼叫通知警報。<br/>
        /// 斷電、被 EMP / Stun 暈眩、或休眠中的反制建築一律不觸發，也不消耗 oneShot 與觸發機率
        /// （判定見 <see cref="AlertBuildingUtility.IsOperational(ThingWithComps)"/>）；<see cref="CanFire"/> 回絕時亦同。
        /// Alarm entry point. An unpowered, EMP/stun-stunned or dormant counter-measure building never fires,
        /// and neither its oneShot nor its trigger roll is consumed; the same goes when <see cref="CanFire"/> refuses.
        /// </summary>
        public void OnAlertNotify()
        {
            OnAlertNotify(null);
        }

        /// <param name="signal">觸發的警報訊號；直接呼叫、沒有訊號時為 null。The alarm signal; null when called directly.</param>
        public void OnAlertNotify(Signal? signal)
        {
            if (!parent.Spawned) return;
            if (!AlertBuildingUtility.IsOperational(parent)) return;
            if (Props.oneShot && hasFired) return;

            // 同 tick 去重：防止多個 Scanner 同 tick 廣播 Signal 導致重複觸發
            int now = Find.TickManager.TicksGame;
            if (lastFiredTick == now) return;

            // 回絕不記 lastFiredTick：同 tick 另一個掃描器的警報若符合條件仍可觸發。
            // A refusal leaves lastFiredTick alone: another scanner's alarm in the same tick may still qualify.
            if (!CanFire(signal)) return;

            if (!Rand.Chance(Props.triggerChance)) return;

            lastFiredTick = now;
            hasFired = true;
            DoEffect();
        }

        /// <summary>
        /// 這次警報要不要觸發，在觸發機率之前判定，回絕不消耗 oneShot 與機率。
        /// 子類覆寫時請保留 base 的判定（目前是 <see cref="CompProperties_AlertEffector.inactiveWhenFogged"/>）。
        /// Whether this alarm should fire the effector; judged before the trigger roll, and a refusal spends neither
        /// oneShot nor the roll. Overrides should keep the base check (currently
        /// <see cref="CompProperties_AlertEffector.inactiveWhenFogged"/>).
        /// </summary>
        /// <param name="signal">觸發的警報訊號；直接呼叫、沒有訊號時為 null。The alarm signal; null when called directly.</param>
        protected virtual bool CanFire(Signal? signal)
        {
            if (Props.inactiveWhenFogged && IsFullyFogged()) return false;
            return true;
        }

        /// <summary>佔地每一格都還在迷霧裡；露出任何一格就算被發現。Every occupied cell fogged; any revealed cell counts as discovered.</summary>
        protected bool IsFullyFogged()
        {
            FogGrid fogGrid = parent.Map.fogGrid;
            foreach (IntVec3 c in parent.OccupiedRect())
            {
                if (!fogGrid.IsFogged(c)) return false;
            }
            return true;
        }

        /// <summary>子類實作具體效果邏輯。</summary>
        protected virtual void DoEffect() { }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref hasFired, "hasFired", false);
        }
    }
}
