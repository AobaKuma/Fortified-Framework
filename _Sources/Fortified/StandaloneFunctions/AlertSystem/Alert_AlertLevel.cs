using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Fortified
{
    // ════════════════════════════════════════════════════════════
    //  Alert_FFF_AlertLevel
    // ════════════════════════════════════════════════════════════
    /// <summary>
    /// 將 <see cref="MapComponent_AlertCounter"/> 的警戒值以原生 Alert（右側通知）方式呈現。
    /// <para>
    /// 顯示條件：地圖上存在至少一個相關警報建築（<see cref="CompAlertScanner"/>）
    /// 且該地圖警戒值 &gt; 0。<br/>
    /// 優先度隨警戒值升級：已觸發 → Critical；≥50% → High；其餘 → Medium。<br/>
    /// 點擊可循環跳轉至各警報建築。
    /// </para>
    /// <remarks>
    /// RimWorld 會自動探索所有 <see cref="Alert"/> 的葉子子類並實例化，無需額外 Def 註冊。
    /// </remarks>
    /// </summary>
    public class Alert_FFF_AlertLevel : Alert
    {
        // ── 快取（於 GetReport 更新，供 Priority / GetLabel / GetExplanation 讀取）──
        private float cachedWorstPct;
        private bool  cachedWorstTriggered;
        private int   cachedBuildingCount;

        public Alert_FFF_AlertLevel()
        {
            defaultLabel = "FFF_Alert_AlertLevel_Label".Translate("0%");
            defaultPriority = AlertPriority.High;
        }

        // 優先度隨最嚴重地圖的警戒狀態動態調整
        public override AlertPriority Priority
        {
            get
            {
                if (cachedWorstTriggered) return AlertPriority.Critical;
                if (cachedWorstPct >= 0.5f) return AlertPriority.High;
                return AlertPriority.Medium;
            }
        }

        public override AlertReport GetReport()
        {
            cachedWorstPct = 0f;
            cachedWorstTriggered = false;
            cachedBuildingCount = 0;

            List<Map> maps = Find.Maps;
            if (maps.NullOrEmpty()) return AlertReport.Inactive;

            // 每次建立新清單，避免跨呼叫的參照別名問題
            List<GlobalTargetInfo> culprits = new List<GlobalTargetInfo>();

            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map == null) continue;

                MapComponent_AlertCounter counter = map.GetComponent<MapComponent_AlertCounter>();
                if (counter == null) continue;

                // 條件一：地圖上存在相關警報建築
                if (!counter.HasActiveScanners) continue;
                // 條件二：警戒值已被拉高（靜止時不打擾玩家）
                if (counter.AlertLevel <= 0f) continue;

                foreach (Thing t in counter.GetScannerBuildings())
                {
                    if (t != null && t.Spawned)
                    {
                        culprits.Add(t);
                        cachedBuildingCount++;
                    }
                }

                if (counter.AlertLevelPct > cachedWorstPct)
                    cachedWorstPct = counter.AlertLevelPct;
                if (counter.IsTriggered)
                    cachedWorstTriggered = true;
            }

            if (culprits.Count == 0) return AlertReport.Inactive;
            return AlertReport.CulpritsAre(culprits);
        }

        public override string GetLabel()
        {
            if (cachedWorstTriggered)
                return "FFF_Alert_AlertLevel_Label_Triggered".Translate();
            return "FFF_Alert_AlertLevel_Label".Translate(cachedWorstPct.ToStringPercent());
        }

        public override TaggedString GetExplanation()
        {
            if (cachedWorstTriggered)
                return "FFF_Alert_AlertLevel_Desc_Triggered".Translate(cachedBuildingCount);
            return "FFF_Alert_AlertLevel_Desc".Translate(
                cachedWorstPct.ToStringPercent(), cachedBuildingCount);
        }
    }
}
