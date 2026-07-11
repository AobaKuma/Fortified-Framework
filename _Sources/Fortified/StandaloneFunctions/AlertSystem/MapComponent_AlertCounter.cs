using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 警戒值效果 Worker 基類。
    /// 由 MapComponent_AlertCounter.AlertEffectWorker 列表定義，
    /// 滿值時從清單中隨機抽一個呼叫 Execute()。
    /// </summary>
    public abstract class AlertEffectWorker
    {
        /// <summary>對應 def 中的 weight，用於加權隨機。</summary>
        public float weight = 1f;

        /// <summary>滿值時由 MapComponent_AlertCounter 呼叫。</summary>
        public abstract void Execute(Map map);
    }

    /// <summary>
    /// 地圖警戒計數器（MapComponent）。
    /// <para>
    /// • 每次 <see cref="CompAlertScanner"/> 偵測到威脅並廣播 Signal 後，
    ///   呼叫 <see cref="Notify(float)"/> 累積警戒值。<br/>
    /// • 8 小時（<see cref="DecayDelay"/> ticks）內無新觸發，警戒值開始線性衰減。<br/>
    /// • 警戒值滿（≥ maxAlertLevel）時呼叫已登錄的 <see cref="AlertEffectWorker"/>（加權隨機）。<br/>
    /// • 支援從多個 Worker 中隨機抽選，方便 MapGenerator 掛載複數效果。
    /// </para>
    /// </summary>
    public partial class MapComponent_AlertCounter : MapComponent
    {
        // ── 常數 ────────────────────────────────────────────────
        /// <summary>警戒值上限（滿值）。</summary>
        public const float MaxAlertLevel = 100f;

        /// <summary>無觸發後開始衰減的等待時長（8 小時）。</summary>
        public const int DecayDelay = GenDate.TicksPerHour * 8;

        /// <summary>每 tick 的衰減速率（預設 100 / (4h) → 4 小時衰完）。</summary>
        public const float DecayPerTick = MaxAlertLevel / (GenDate.TicksPerHour * 4f);

        // ── 序列化欄位 ──────────────────────────────────────────
        private float alertLevel = 0f;
        private int lastNotifyTick = -99999;
        private bool triggered = false;

        // ── 執行期欄位（不需持久化）──────────────────────────────
        /// <summary>
        /// 可在 MapGenerator 或地圖生成後動態掛載的效果清單。
        /// 滿值時從中加權隨機選一個執行。
        /// </summary>
        public List<AlertEffectWorker> effectWorkers = new List<AlertEffectWorker>();

        /// <summary>
        /// 本地圖已生成的警報掃描建築（<see cref="CompAlertScanner"/>）登錄表。
        /// <para>
        /// • 由 <see cref="CompAlertScanner.PostSpawnSetup"/> / <see cref="CompAlertScanner.PostDeSpawn"/>
        ///   維護，供 <see cref="Alert_FFF_AlertLevel"/> 判定「地圖上是否存在相關警報建築」。<br/>
        /// • 純執行期資料，不序列化；讀檔後各 Comp 於 PostSpawnSetup 重新登錄。<br/>
        /// • 讀取時會就地剔除已失效（null / 未生成）的項目，避免殘留參照。
        /// </para>
        /// </summary>
        private readonly List<CompAlertScanner> registeredScanners = new List<CompAlertScanner>();

        // ── 屬性 ────────────────────────────────────────────────
        public float AlertLevel => alertLevel;
        public float AlertLevelPct => alertLevel / MaxAlertLevel;
        public bool IsTriggered => triggered;

        /// <summary>本地圖是否存在至少一個有效（已生成）的警報掃描建築。</summary>
        public bool HasActiveScanners
        {
            get
            {
                for (int i = registeredScanners.Count - 1; i >= 0; i--)
                {
                    CompAlertScanner s = registeredScanners[i];
                    if (s?.parent == null || !s.parent.Spawned)
                    {
                        registeredScanners.RemoveAt(i);   // 就地剔除失效項
                        continue;
                    }
                    return true;
                }
                return false;
            }
        }

        public MapComponent_AlertCounter(Map map) : base(map) { }

        // ── 掃描建築登錄 ────────────────────────────────────────
        /// <summary>由 <see cref="CompAlertScanner"/> 生成時呼叫（防重複、防 null）。</summary>
        public void RegisterScanner(CompAlertScanner scanner)
        {
            if (scanner == null) return;
            if (!registeredScanners.Contains(scanner))
                registeredScanners.Add(scanner);
        }

        /// <summary>由 <see cref="CompAlertScanner"/> 移除時呼叫。</summary>
        public void UnregisterScanner(CompAlertScanner scanner)
        {
            if (scanner == null) return;
            registeredScanners.Remove(scanner);
        }

        /// <summary>
        /// 列舉目前有效的警報掃描建築（供 Alert 作為點擊跳轉目標）。
        /// 列舉過程中就地剔除已失效的登錄項。
        /// </summary>
        public IEnumerable<Thing> GetScannerBuildings()
        {
            for (int i = registeredScanners.Count - 1; i >= 0; i--)
            {
                CompAlertScanner s = registeredScanners[i];
                if (s?.parent == null || !s.parent.Spawned)
                {
                    registeredScanners.RemoveAt(i);
                    continue;
                }
                yield return s.parent;
            }
        }

        // ── 公開介面 ────────────────────────────────────────────
        /// <summary>
        /// 由 CompAlertScanner 呼叫。amount 預設 +20（即 5 次觸滿）。
        /// </summary>
        public void Notify(float amount = 20f)
        {
            if (triggered) return;
            lastNotifyTick = Find.TickManager.TicksGame;
            alertLevel = Mathf.Min(alertLevel + amount, MaxAlertLevel);
            CheckTrigger();
        }

        /// <summary>
        /// 強制重置警戒值（例如玩家成功摧毀所有掃描器）。
        /// </summary>
        public void Reset()
        {
            alertLevel = 0f;
            triggered = false;
            lastNotifyTick = -99999;
        }

        // ── MapComponent 覆寫 ───────────────────────────────────
        public override void MapComponentTick()
        {
            // 持續效果 Tick（空域封鎖、精確砲擊）
            TickActiveEffects();

            if (triggered) return;

            // 衰減：距上次 Notify 超過 DecayDelay 才開始
            int now = Find.TickManager.TicksGame;
            if (alertLevel > 0f && (now - lastNotifyTick) > DecayDelay)
            {
                alertLevel = Mathf.Max(0f, alertLevel - DecayPerTick);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref alertLevel, "alertLevel", 0f);
            Scribe_Values.Look(ref lastNotifyTick, "lastNotifyTick", -99999);
            Scribe_Values.Look(ref triggered, "triggered", false);
        }

        // ── 私有邏輯 ────────────────────────────────────────────
        private void CheckTrigger()
        {
            if (alertLevel < MaxAlertLevel) return;
            triggered = true;
            ExecuteEffect();
        }

        private void ExecuteEffect()
        {
            if (effectWorkers.NullOrEmpty())
            {
                Log.Warning($"[FFF] MapComponent_AlertCounter on map {map} triggered but no effectWorkers registered.");
                return;
            }

            // 加權隨機選一個效果執行
            AlertEffectWorker chosen = effectWorkers
                .Where(w => w != null && w.weight > 0f)
                .RandomElementByWeightWithFallback(w => w.weight);

            if (chosen == null)
            {
                Log.Warning("[FFF] MapComponent_AlertCounter: no valid effectWorker found.");
                return;
            }

            try
            {
                chosen.Execute(map);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[FFF] AlertEffectWorker.Execute threw: {ex}");
            }
        }
    }
}
