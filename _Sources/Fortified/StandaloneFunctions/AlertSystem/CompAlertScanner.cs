using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Fortified
{
    // ════════════════════════════════════════════════════════════
    //  CompProperties_AlertScanner
    // ════════════════════════════════════════════════════════════
    /// <summary>
    /// 範圍偵測掃描器的 CompProperties。
    /// 支援矩形（width × height）或圓形（radius）掃描範圍，並可選擇是否受 LOS 影響。
    /// </summary>
    public class CompProperties_AlertScanner : CompProperties
    {
        // ── 範圍設定 ────────────────────────────────────────────
        /// <summary>矩形偵測寬度（以 parent 為中心，0 表示使用 radius）。</summary>
        public int scanWidth = 0;
        /// <summary>矩形偵測高度（以 parent 為中心，0 表示使用 radius）。</summary>
        public int scanHeight = 0;
        /// <summary>圓形偵測半徑（scanWidth/Height 為 0 時生效）。</summary>
        public int radius = 0;
        /// <summary>是否受視線（LOS）影響。</summary>
        public bool requireLOS = true;

        // ── 旋轉補正（Attachment 用）────────────────────────────
        /// <summary>
        /// 掃描方向相對於 parent.Rotation 的補正量（Rot4 步數，0~3）。<br/>
        /// 0 = 不補正（預設）。<br/>
        /// 1 = 順時針 90°。<br/>
        /// 2 = 180°（反向）。<br/>
        /// 3 = 逆時針 90°。<br/>
        /// 當 Camera 作為 Attachment 使用、其 Rotation 與實際面向不符時設定此值。
        /// </summary>
        public int scanRotationOffset = 0;

        // ── 行為設定 ────────────────────────────────────────────
        /// <summary>掃描間隔（ticks），預設 250（TickRare）。</summary>
        public int checkInterval = 250;
        /// <summary>觸發後重新武裝的間隔（ticks），預設 2500（約 42 秒）。</summary>
        public int rearmInterval = 2500;
        /// <summary>
        /// 被攻擊時觸發的機率（0~1）。
        /// 0 代表被攻擊不主動觸發，1 代表必定觸發。
        /// </summary>
        public float attackTriggerChance = 0.5f;
        /// <summary>掃描到威脅後，倒數這麼多 ticks 後才廣播 Signal（0 = 立即）。</summary>
        public int triggerCountdown = 0;
        /// <summary>每次 Notify 給 AlertCounter 增加的量。</summary>
        public float alertIncrement = 20f;

        // ── Signal 設定 ────────────────────────────────────────
        /// <summary>廣播的 Signal 字串（給其他 Comp 接收）。</summary>
        public string signalString = "FFF_AlertScanner_Triggered";

        // ── 常態 Effecter（三態）──────────────────────────────
        /// <summary>正常監視中（有電、已武裝、未偵測）時持續播放。</summary>
        public EffecterDef watchingEffecter;
        /// <summary>偵測到威脅時持續播放。</summary>
        public EffecterDef detectionEffecter;
        /// <summary>離線（斷電或 EMP 癱瘓）時持續播放。</summary>
        public EffecterDef offlineEffecter;

        // ── 一次性觸發效果 ──────────────────────────────────────
        /// <summary>倒數結束並廣播 Signal 瞬間播放（一次性）。</summary>
        public EffecterDef triggerEffector;

        public CompProperties_AlertScanner()
        {
            compClass = typeof(CompAlertScanner);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  CompAlertScanner
    // ════════════════════════════════════════════════════════════
    /// <summary>
    /// 範圍偵測器。偵測到敵方 Pawn 後廣播 Signal 並通知地圖警戒計數器。
    /// <para>
    /// 掃描形狀：<br/>
    ///   • scanWidth × scanHeight > 0 → 矩形<br/>
    ///   • radius > 0 → 圓形<br/>
    /// 視線：<see cref="CompProperties_AlertScanner.requireLOS"/> 控制。<br/>
    /// 電力：有 <see cref="CompPowerTrader"/> 時斷電停止掃描。<br/>
    /// EMP：有 <see cref="CompCanBeDormant"/> 時被 EMP 癱瘓後停止掃描。
    /// </para>
    /// </summary>
    public class CompAlertScanner : ThingComp
    {
        // ── 快取（Comp 引用）────────────────────────────────────
        private CompPowerTrader cachedPower;
        private CompCanBeDormant cachedDormant;

        // ── 掃描格快取 ───────────────────────────────────────────
        /// <summary>幾何格（不含 LOS 過濾）。當位置或旋轉改變時重建。</summary>
        private List<IntVec3> cachedGeoCells;
        /// <summary>LOS 可視格（requireLOS=true 時為 cachedGeoCells 的子集）。</summary>
        private List<IntVec3> cachedLosCells;
        /// <summary>上次快取幾何格時的中心座標。</summary>
        private IntVec3 geoCacheOrigin = IntVec3.Invalid;
        /// <summary>上次快取幾何格時的旋轉。</summary>
        private int geoCacheRot = -1;
        /// <summary>下次重建 LOS 快取的 tick。</summary>
        private int nextLosCacheTick = -1;

        // ── 狀態 ────────────────────────────────────────────────
        private bool detecting = false;          // 目前是否偵測到威脅
        private int rearmTicksLeft = 0;          // 重新武裝剩餘 ticks
        private int countdownTicksLeft = 0;      // 觸發倒數剩餘 ticks
        private bool countingDown = false;
        private int nextCheckTick = 0;

        // ── Effecter 實例（常態三態 + 一次性觸發）──────────────
        private Effecter watchingEffecterInst;
        private Effecter detectionEffecterInst;
        private Effecter offlineEffecterInst;
        private Effecter triggerEffecterInst;

        /// <summary>目前活躍的常態狀態（避免每 tick 重建 Effecter）。</summary>
        private enum ScannerState { None, Watching, Detecting, Offline }
        private ScannerState currentEffecterState = ScannerState.None;

        // ── 屬性 ────────────────────────────────────────────────
        public CompProperties_AlertScanner Props => (CompProperties_AlertScanner)props;

        private bool IsOperational
        {
            get
            {
                if (cachedPower != null && !cachedPower.PowerOn) return false;
                if (cachedDormant != null && !cachedDormant.Awake) return false;
                return true;
            }
        }

        private bool IsArmed => rearmTicksLeft <= 0;

        // ── 生命週期 ────────────────────────────────────────────
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            cachedPower   = parent.GetComp<CompPowerTrader>();
            cachedDormant = parent.GetComp<CompCanBeDormant>();
            nextCheckTick = Find.TickManager.TicksGame + Props.checkInterval;
            InvalidateScanCache();
            // 登錄到地圖警戒計數器（供 Alert 判定「地圖存在相關警報建築」）
            parent.Map?.GetComponent<MapComponent_AlertCounter>()?.RegisterScanner(this);
            // 強制刷新一次狀態 Effecter（讀檔後恢復正確視覺）
            currentEffecterState = ScannerState.None;
            UpdateStateEffecter();
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            // 自地圖警戒計數器登錄表移除
            map?.GetComponent<MapComponent_AlertCounter>()?.UnregisterScanner(this);
            InvalidateScanCache();
            CleanupEffecters();
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned) return;

            // 冷卻倒計時
            if (rearmTicksLeft > 0)
            {
                rearmTicksLeft--;
            }

            // 觸發倒數
            if (countingDown)
            {
                if (countdownTicksLeft > 0)
                {
                    countdownTicksLeft--;
                }
                else
                {
                    countingDown = false;
                    FireSignal();
                }
            }

            // 定期掃描
            if (Find.TickManager.TicksGame >= nextCheckTick)
            {
                nextCheckTick = Find.TickManager.TicksGame + Props.checkInterval;
                if (IsOperational)
                {
                    RunScan();
                }
                else
                {
                    SetDetecting(false);
                }
            }

            // 更新常態 Effecter 狀態並 Tick 當前活躍的實例
            UpdateStateEffecter();
            TargetInfo tgt = new TargetInfo(parent.Position, parent.Map);
            watchingEffecterInst?.EffectTick(tgt, tgt);
            detectionEffecterInst?.EffectTick(tgt, tgt);
            offlineEffecterInst?.EffectTick(tgt, tgt);
        }

        // ── 被攻擊通知 ──────────────────────────────────────────
        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PostPreApplyDamage(ref dinfo, out absorbed);
            if (!IsOperational || !IsArmed) return;
            if (Rand.Chance(Props.attackTriggerChance))
            {
                BeginCountdownOrFire();
            }
        }

        // ── Gizmo（開發模式）───────────────────────────────────
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!DebugSettings.ShowDevGizmos) yield break;
            yield return new Command_Action
            {
                defaultLabel = "DEV: Trigger Scanner",
                action = BeginCountdownOrFire
            };
            yield return new Command_Action
            {
                defaultLabel = "DEV: Reset Rearm",
                action = () => rearmTicksLeft = 0
            };
        }

        // ── 選取時顯示掃描範圍 ────────────────────────────────────
        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            if (!parent.Spawned) return;

            List<IntVec3> geo = GetGeoCells();
            if (geo.Count == 0) return;

            // 狀態色：運作中=青、冷卻=黃、離線=灰
            Color statusColor = IsOperational
                ? (IsArmed ? Color.cyan : Color.yellow)
                : Color.gray;

            if (Props.requireLOS)
            {
                // 先畫全幾何範圍（半透明灰），再畫 LOS 可視格（狀態色）
                Color dimColor = new Color(0.4f, 0.4f, 0.4f, 0.25f);
                GenDraw.DrawFieldEdges(geo, dimColor);
                List<IntVec3> los = GetLosCells();
                if (los.Count > 0)
                    GenDraw.DrawFieldEdges(los, statusColor);
            }
            else
            {
                GenDraw.DrawFieldEdges(geo, statusColor);
            }
        }

        // ── InspectString ────────────────────────────────────────
        public override string CompInspectStringExtra()
        {
            if (!IsOperational)
                return "FFF_AlertScanner_Offline".Translate().Colorize(Color.gray);
            if (!IsArmed)
                return "FFF_AlertScanner_Rearming".Translate(rearmTicksLeft.ToStringTicksToPeriod()).Colorize(Color.yellow);
            if (detecting)
                return "FFF_AlertScanner_Detecting".Translate().Colorize(ColorLibrary.RedReadable);
            return "FFF_AlertScanner_Watching".Translate();
        }

        // ── 存檔 ─────────────────────────────────────────────────
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref detecting, "detecting", false);
            Scribe_Values.Look(ref rearmTicksLeft, "rearmTicksLeft", 0);
            Scribe_Values.Look(ref countdownTicksLeft, "countdownTicksLeft", 0);
            Scribe_Values.Look(ref countingDown, "countingDown", false);
            Scribe_Values.Look(ref nextCheckTick, "nextCheckTick", 0);
        }

        // ── 範圍繪製（PlaceWorker 使用）────────────────────────
        /// <summary>
        /// 傳回掃描格列表。<br/>
        /// <paramref name="overrideRot"/> 供 PlaceWorker 傳入放置預覽旋轉；
        /// <paramref name="overrideCenter"/> 供 PlaceWorker 傳入放置中心；
        /// 兩者 null 時使用 parent 的實際值。
        /// </summary>
        public IEnumerable<IntVec3> ScanCells(Rot4? overrideRot = null, IntVec3? overrideCenter = null)
        {
            IntVec3 center = overrideCenter ?? parent.Position;

            // 套用 scanRotationOffset 補正
            Rot4 baseRot = overrideRot ?? parent.Rotation;
            Rot4 effectiveRot = new Rot4((baseRot.AsInt + Props.scanRotationOffset) % 4);

            if (Props.scanWidth > 0 && Props.scanHeight > 0)
            {
                int w = (Props.scanWidth - 1) / 2;
                int h = Props.scanHeight - 1;
                IntVec3 forward = effectiveRot.FacingCell;
                IntVec3 right = effectiveRot.RighthandCell;
                for (int fwd = 0; fwd <= h; fwd++)
                {
                    for (int side = -w; side <= w; side++)
                    {
                        yield return center + forward * fwd + right * side;
                    }
                }
            }
            else if (Props.radius > 0)
            {
                // 圓形範圍不受旋轉影響，直接展開
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, Props.radius, true))
                {
                    yield return cell;
                }
            }
        }

        // ── 私有邏輯 ────────────────────────────────────────────
        private void RunScan()
        {
            if (!IsArmed) return;
            bool found = false;
            Map m = parent.Map;

            // 使用快取格列表（requireLOS 時已預先過濾視線，不再逐格呼叫 LineOfSight）
            List<IntVec3> cells = Props.requireLOS ? GetLosCells() : GetGeoCells();

            for (int ci = 0; ci < cells.Count; ci++)
            {
                IntVec3 cell = cells[ci];
                List<Thing> things = m.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Pawn pawn
                        && pawn.Faction != null
                        && pawn.Faction != parent.Faction
                        && pawn.Faction.HostileTo(parent.Faction ?? Faction.OfPlayer)
                        && !pawn.Dead
                        && !pawn.Downed)
                    {
                        found = true;
                        break;
                    }
                }
                if (found) break;
            }

            SetDetecting(found);
            if (found) BeginCountdownOrFire();
        }

        // ── 掃描格快取 ───────────────────────────────────────────
        /// <summary>清除兩層快取（位置改變、Despawn 時呼叫）。</summary>
        private void InvalidateScanCache()
        {
            cachedGeoCells = null;
            cachedLosCells = null;
            geoCacheOrigin = IntVec3.Invalid;
            geoCacheRot    = -1;
            nextLosCacheTick = -1;
        }

        /// <summary>
        /// 取得幾何格列表（不含 LOS）。
        /// 位置或旋轉改變時自動重建；否則直接回傳快取。
        /// </summary>
        private List<IntVec3> GetGeoCells()
        {
            IntVec3 pos = parent.Position;
            int rot     = parent.Rotation.AsInt;

            if (cachedGeoCells == null || pos != geoCacheOrigin || rot != geoCacheRot)
            {
                Map m = parent.Map;
                cachedGeoCells = ScanCells()
                    .Where(c => c.InBounds(m))
                    .ToList();
                geoCacheOrigin   = pos;
                geoCacheRot      = rot;
                // 幾何改變時 LOS 也需要重建
                cachedLosCells   = null;
                nextLosCacheTick = -1;
            }
            return cachedGeoCells;
        }

        /// <summary>
        /// 取得 LOS 可視格列表。
        /// 每 <see cref="CompProperties_AlertScanner.checkInterval"/> ticks 重建一次；
        /// requireLOS=false 時直接回傳幾何格。
        /// </summary>
        private List<IntVec3> GetLosCells()
        {
            if (!Props.requireLOS) return GetGeoCells();

            int now = Find.TickManager.TicksGame;
            if (cachedLosCells == null || now >= nextLosCacheTick)
            {
                Map m          = parent.Map;
                IntVec3 origin = parent.Position;
                List<IntVec3> geo = GetGeoCells();

                cachedLosCells = new List<IntVec3>(geo.Count);
                for (int i = 0; i < geo.Count; i++)
                {
                    if (GenSight.LineOfSight(origin, geo[i], m, true))
                        cachedLosCells.Add(geo[i]);
                }
                nextLosCacheTick = now + Props.checkInterval;
            }
            return cachedLosCells;
        }

        private void SetDetecting(bool value)
        {
            if (detecting == value) return;
            detecting = value;
            // Effecter 切換由 CompTick 內的 UpdateStateEffecter() 統一處理
        }

        private void BeginCountdownOrFire()
        {
            if (!IsArmed || countingDown) return;
            if (Props.triggerCountdown > 0)
            {
                countingDown = true;
                countdownTicksLeft = Props.triggerCountdown;
            }
            else
            {
                FireSignal();
            }
        }

        private void FireSignal()
        {
            if (!parent.Spawned) return;

            // 累積地圖警戒值
            parent.Map.GetComponent<MapComponent_AlertCounter>()?.Notify(Props.alertIncrement);

            // 廣播 Signal（讓同地圖的 CompAlertEffector 等接收）
            if (!Props.signalString.NullOrEmpty())
            {
                Find.SignalManager.SendSignal(new Signal(
                    Props.signalString,
                    parent.Named("SUBJECT"),
                    parent.Position.Named("POSITION"),
                    parent.Map.Named("MAP")));
            }

            // 觸發 Effecter
            if (Props.triggerEffector != null)
            {
                TargetInfo tgt = new TargetInfo(parent.Position, parent.Map);
                triggerEffecterInst?.Cleanup();
                triggerEffecterInst = Props.triggerEffector.Spawn(parent.Position, parent.Map);
            }

            // 進入重新武裝冷卻
            rearmTicksLeft = Props.rearmInterval;
            countingDown = false;
        }

        /// <summary>
        /// 根據目前運作狀態切換常態 Effecter。
        /// 狀態優先級：Offline > Detecting > Watching。
        /// 僅在狀態變化時重建 Effecter，避免每 tick 多餘的 Spawn/Cleanup。
        /// </summary>
        private void UpdateStateEffecter()
        {
            ScannerState desired;
            if (!IsOperational)
                desired = ScannerState.Offline;
            else if (detecting)
                desired = ScannerState.Detecting;
            else
                desired = ScannerState.Watching;

            if (desired == currentEffecterState) return;

            // 清除舊狀態 Effecter
            watchingEffecterInst?.Cleanup();   watchingEffecterInst   = null;
            detectionEffecterInst?.Cleanup();  detectionEffecterInst  = null;
            offlineEffecterInst?.Cleanup();    offlineEffecterInst    = null;

            // 建立新狀態 Effecter
            if (parent.Spawned)
            {
                switch (desired)
                {
                    case ScannerState.Watching:
                        if (Props.watchingEffecter != null)
                            watchingEffecterInst = Props.watchingEffecter.Spawn(parent.Position, parent.Map);
                        break;
                    case ScannerState.Detecting:
                        if (Props.detectionEffecter != null)
                            detectionEffecterInst = Props.detectionEffecter.Spawn(parent.Position, parent.Map);
                        break;
                    case ScannerState.Offline:
                        if (Props.offlineEffecter != null)
                            offlineEffecterInst = Props.offlineEffecter.Spawn(parent.Position, parent.Map);
                        break;
                }
            }

            currentEffecterState = desired;
        }

        private void CleanupEffecters()
        {
            watchingEffecterInst?.Cleanup();   watchingEffecterInst   = null;
            detectionEffecterInst?.Cleanup();  detectionEffecterInst  = null;
            offlineEffecterInst?.Cleanup();    offlineEffecterInst    = null;
            triggerEffecterInst?.Cleanup();    triggerEffecterInst    = null;
            currentEffecterState = ScannerState.None;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  PlaceWorker：顯示掃描範圍
    // ════════════════════════════════════════════════════════════
    public class PlaceWorker_AlertScanner : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            CompProperties_AlertScanner props = def.GetCompProperties<CompProperties_AlertScanner>();
            if (props == null) return;

            // 若已放置（thing != null），直接用 Comp 的 ScanCells 以確保與選取顯示完全一致
            if (thing?.TryGetComp<CompAlertScanner>() is CompAlertScanner comp)
            {
                GenDraw.DrawFieldEdges(
                    comp.ScanCells().Where(c => c.InBounds(thing.Map)).ToList(),
                    Color.cyan);
                return;
            }

            // 放置預覽：套用 scanRotationOffset 補正
            Rot4 effectiveRot = new Rot4((rot.AsInt + props.scanRotationOffset) % 4);
            List<IntVec3> cells = new List<IntVec3>();

            if (props.scanWidth > 0 && props.scanHeight > 0)
            {
                int w = (props.scanWidth - 1) / 2;
                int h = props.scanHeight - 1;
                IntVec3 forward = effectiveRot.FacingCell;
                IntVec3 right = effectiveRot.RighthandCell;
                for (int fwd = 0; fwd <= h; fwd++)
                    for (int side = -w; side <= w; side++)
                        cells.Add(center + forward * fwd + right * side);
            }
            else if (props.radius > 0)
            {
                cells.AddRange(GenRadial.RadialCellsAround(center, props.radius, true));
            }

            GenDraw.DrawFieldEdges(cells, Color.cyan);
        }
    }
}
