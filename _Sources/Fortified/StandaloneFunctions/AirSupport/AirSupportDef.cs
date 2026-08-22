using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified
{
    public class AirSupportDef : Def
    {
        public List<AirSupportComp> comps = new List<AirSupportComp>();

        public Vector3 tempOriginCache = Vector3.zero;

        public bool originOverridedCache;

        /// <summary>
        /// 呼叫成功時記錄的軼事。只有在呼叫者是玩家派系的 pawn 時才會記錄。
        /// 框架只傳一個 pawn 進去，所以 taleClass 必須是 Tale_SinglePawn
        /// （TaleFactory 用 Activator.CreateInstance，參數數量不對會直接噴 error）。
        ///
        /// The tale recorded when this support is successfully called in.
        /// Only one pawn is passed, so the taleClass must be Tale_SinglePawn.
        /// </summary>
        public TaleDef taleOnTriggered;

        /// <summary>
        /// 呼叫成功時記錄的歷史事件，供信仰 precept 監聽。同樣只對玩家派系記錄。
        ///
        /// The history event recorded when this support is called in, for ideoligion precepts.
        /// </summary>
        public HistoryEventDef historyEventOnTriggered;

        public void Trigger(Thing trigger, Map map, LocalTargetInfo target)
        {
            RecordCallerEvents(trigger);
            originOverridedCache = tempOriginCache != Vector3.zero;
            foreach (AirSupportComp comp in comps)
            {
                comp.Trigger(this, trigger, map, target);
            }
            //Clear cache afterwards so that it's possible to set origin beforehand.
            tempOriginCache = Vector3.zero;
        }
        public void DrawHighlight(Map map, IntVec3 callerPos, LocalTargetInfo target)
        {
            foreach (AirSupportComp comp in comps)
            {
                comp.DrawHighlight(map, callerPos, target);
            }
        }

        /// <summary>
        /// Trigger 是所有呼叫路徑（CompAirSupportSummoner、
        /// RoyalTitlePermitWorker_CallAirSupport、以及任何走 AirSupportDef 的能力）
        /// 唯一的匯流點，所以紀錄掛在這裡就不會重複也不會漏。
        /// 敵方或建築觸發的支援不記錄。
        /// </summary>
        private void RecordCallerEvents(Thing trigger)
        {
            if (taleOnTriggered == null && historyEventOnTriggered == null)
            {
                return;
            }

            Pawn caller = trigger as Pawn;
            if (caller == null || caller.Faction != Faction.OfPlayer)
            {
                return;
            }

            if (taleOnTriggered != null)
            {
                TaleRecorder.RecordTale(taleOnTriggered, caller);
            }

            if (historyEventOnTriggered != null)
            {
                Find.HistoryEventsManager.RecordEvent(
                    new HistoryEvent(historyEventOnTriggered, caller.Named(HistoryEventArgsNames.Doer)));
            }
        }
    }
}
