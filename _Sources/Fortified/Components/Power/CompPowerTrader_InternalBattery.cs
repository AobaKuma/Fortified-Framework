using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified
{
    public class CompProperties_PowerWithInternalBattery : CompProperties_Power
    {
        public float internalBatteryMax = 50f;
        public float chargeRateWatts = 15f;
        /// <summary>
        /// 開關撥到關閉（待機）時的充電速率倍率，以 <see cref="chargeRateWatts"/> 為基準。
        /// 待機時本體不耗電，省下的功率全數拿去充電，因此預設 2 倍。
        /// </summary>
        public float standbyChargeFactor = 2f;

        /// <summary>
        /// 非玩家陣營的建築生成時，內建電池的初始電量比例（隨機取值）。
        /// 讓遺跡／站點裡的砲塔與感測器一落地就有電，不必等電網。設成 0~0 停用。
        /// Initial charge fraction rolled when a non-player-faction building spawns, so ruin and site
        /// turrets / sensors are live on arrival instead of waiting for a grid. 0~0 disables it.
        /// </summary>
        public FloatRange npcInitialChargePct = new FloatRange(0.5f, 1f);
        public CompProperties_PowerWithInternalBattery()
        {
            compClass = typeof(CompPowerTrader_InternalBattery);
        }
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
                yield return e;

            if (internalBatteryMax <= 0f)
                yield return $"{nameof(CompProperties_PowerWithInternalBattery)}: internalBatteryMax must be > 0.";
            if (chargeRateWatts < 0f)
                yield return $"{nameof(CompProperties_PowerWithInternalBattery)}: chargeRateWatts must be >= 0.";
            if (standbyChargeFactor < 0f)
                yield return $"{nameof(CompProperties_PowerWithInternalBattery)}: standbyChargeFactor must be >= 0.";
        }
    }
    /// <summary>
    /// 帶內部電池的耗電端。三種狀態：
    ///   1. 電網供電：正常運作，同時以 chargeRateWatts 充電。
    ///   2. 自供電：電網斷電但電池有電 → 靠電池維持運作（PowerOn 由本 comp 撐住）。
    ///   3. 待機：開關撥到關閉。PowerOn 交給原版維持 false（砲塔不射擊、燈不亮），
    ///      不進入自供電，改以 chargeRateWatts × standbyChargeFactor 向電網純充電。
    ///      關閉狀態下原版 PowerNet 不會替我們申報功率，所以待機充電是直接從電網的電池抽取，
    ///      沒有電池的電網則取用當下的發電盈餘（盈餘本來就會被丟棄）。
    /// </summary>
    public class CompPowerTrader_InternalBattery : CompPowerTrader
    {
        public const string Signal_SelfPoweredOn  = "FFF_SelfPoweredOn";
        public const string Signal_SelfPoweredOff = "FFF_SelfPoweredOff";

        private float storedEnergy;   // Watt-days
        private bool  selfPowered;    // true = running off internal battery
        private float standbyChargeWattsLast; // 上一 tick 待機實際充入的功率，僅供顯示

        private CompProperties_PowerWithInternalBattery BatteryProps =>
            (CompProperties_PowerWithInternalBattery)props;
        public float StoredEnergy    => storedEnergy;
        public float StoredEnergyPct => storedEnergy / BatteryProps.internalBatteryMax;
        public bool  SelfPowered     => selfPowered;
        /// <summary>開關撥到關閉：待機純充電，不運作。</summary>
        public bool  Standby         => parent.Spawned && !FlickUtility.WantsToBeOn(parent);
        public float StandbyChargeWatts => BatteryProps.chargeRateWatts * BatteryProps.standbyChargeFactor;
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref storedEnergy, nameof(storedEnergy), 0f);
            Scribe_Values.Look(ref selfPowered,  nameof(selfPowered),  false);

            // Guard against corrupt saves.
            storedEnergy = Mathf.Clamp(storedEnergy, 0f, BatteryProps.internalBatteryMax);
        }
        public override void SetUpPowerVars()
        {
            base.SetUpPowerVars(); // sets PowerOutput = -basePowerConsumption
            SyncPowerOutput();
        }

        /// <summary>
        /// 直接設定內建電量（0~1 比例）。給生成流程用，例如儲存庫的警戒設施要一落地就滿電。
        /// Sets the stored charge as a 0~1 fraction. For generation code that wants a fixture live on spawn.
        /// </summary>
        public void SetStoredEnergyPct(float pct)
        {
            storedEnergy = BatteryProps.internalBatteryMax * Mathf.Clamp01(pct);
            SyncPowerOutput();
        }

        /// <summary>
        /// 非玩家陣營的新生成建築：電池若還是空的，依 npcInitialChargePct 隨機灌入初始電量。
        /// 讀檔重生與玩家自己的建築不擲骰；生成流程若已先設定電量（storedEnergy &gt; 0）也不覆寫。
        /// Fresh spawns owned by a non-player faction get a random initial charge from npcInitialChargePct
        /// if the battery is still empty. Skipped on load, for player-owned things, and when generation
        /// code already set a charge before spawning.
        /// </summary>
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (respawningAfterLoad || storedEnergy > 0f) return;

            Faction faction = parent.Faction;
            if (faction == null || faction == Faction.OfPlayer) return;

            FloatRange range = BatteryProps.npcInitialChargePct;
            if (range.max <= 0f) return;
            SetStoredEnergyPct(range.RandomInRange);
        }
        private void SyncPowerOutput()
        {
            if (selfPowered && storedEnergy > 0f)
            {
                PowerOutput = 0f;
            }
            else if (!selfPowered && storedEnergy < BatteryProps.internalBatteryMax)
            {
                PowerOutput = -(Props.PowerConsumption + BatteryProps.chargeRateWatts);
            }
            else
            {
                PowerOutput = -Props.PowerConsumption;
            }
        }

        // ── tick ──────────────────────────────────────────────────────────────────
        /// <summary>
        /// 完整邏輯每 <see cref="UpdateInterval"/> tick 才跑一次（依建築雜湊錯開），電量依實際經過 tick 數結算。
        /// 斷電／開關訊號會標記 <see cref="dirty"/>，下一 tick 立刻處理，切換自供電不會延遲。
        /// Full logic runs every UpdateInterval ticks (hash-staggered) with energy scaled by elapsed ticks;
        /// power/flick signals mark the comp dirty so switch-over still happens on the next tick.
        /// </summary>
        private const int UpdateInterval = 60;
        private bool dirty = true;
        private int  lastUpdateTick = -1;

        public override void ReceiveCompSignal(string signal)
        {
            base.ReceiveCompSignal(signal);
            switch (signal)
            {
                case "PowerTurnedOff":
                case "PowerTurnedOn":
                case "FlickedOff":
                case "FlickedOn":
                case "ScheduledOff":
                case "ScheduledOn":
                    dirty = true;
                    break;
            }
        }
        public override void CompTick()
        {
            base.CompTick();

            if (!dirty && !parent.IsHashIntervalTick(UpdateInterval)) return;
            if (!parent.Spawned) return;

            int now = Find.TickManager.TicksGame;
            int elapsed = lastUpdateTick < 0 ? 1 : Mathf.Clamp(now - lastUpdateTick, 1, UpdateInterval);
            lastUpdateTick = now;
            dirty = false;
            float tickFactor = WattsToWattDaysPerTick * elapsed;

            if (Standby)
            {
                StandbyTick(elapsed);
                return;
            }
            standbyChargeWattsLast = 0f;

            if (PowerOn)
            {
                if (selfPowered)
                {
                    if (GridCanSustainUs())
                    {
                        ExitSelfPowered();
                        return;
                    }
                    storedEnergy -= Props.PowerConsumption * tickFactor;
                    if (storedEnergy <= 0f)
                    {
                        storedEnergy = 0f;
                        ExitSelfPowered();
                        PowerOn = false;
                    }
                }
                else
                {
                    // 停電事件開始時原版要隨機幾輪才會把我們關掉；直接斷開，下一 tick 就切自供電。
                    if (parent.Map.gameConditionManager.ElectricityDisabled(parent.Map))
                    {
                        PowerOn = false;
                        return;
                    }
                    if (storedEnergy < BatteryProps.internalBatteryMax)
                    {
                        float charge = BatteryProps.chargeRateWatts * tickFactor;
                        storedEnergy = Mathf.Min(storedEnergy + charge, BatteryProps.internalBatteryMax);
                        SyncPowerOutput();
                    }
                }
            }
            else
            {
                if (storedEnergy > 0f)
                {
                    if (!selfPowered)
                    {
                        selfPowered = true;
                        SyncPowerOutput(); // sets PowerOutput = 0 immediately
                        parent.BroadcastCompSignal(Signal_SelfPoweredOn);
                    }
                    PowerOn = true;    // re-activates glow via BroadcastCompSignal
                }
            }
        }
        private void ExitSelfPowered()
        {
            selfPowered = false;
            SyncPowerOutput(); // restore −basePower immediately
            parent.BroadcastCompSignal(Signal_SelfPoweredOff);
        }
        /// <summary>
        /// 待機（開關關閉）：不運作、不自供電，只向電網充電。
        /// 原版收到 FlickedOff 時已把 PowerOn 設為 false；這裡確保不會被自供電邏輯重新拉起。
        /// </summary>
        private void StandbyTick(int elapsed)
        {
            if (selfPowered)
            {
                ExitSelfPowered();
            }
            if (PowerOn)
            {
                PowerOn = false; // 舊存檔可能殘留待機前自供電時撐住的 PowerOn
            }
            standbyChargeWattsLast = 0f;

            float max = BatteryProps.internalBatteryMax;
            if (storedEnergy >= max) return;

            PowerNet net = PowerNet;
            if (net == null) return;
            if (parent.Map.gameConditionManager.ElectricityDisabled(parent.Map)) return;

            float tickFactor = WattsToWattDaysPerTick * elapsed;
            float want = Mathf.Min(StandbyChargeWatts * tickFactor, max - storedEnergy);
            if (want <= 0f) return;

            float got = DrawFromNet(net, want, elapsed);
            if (got <= 0f) return;

            storedEnergy = Mathf.Min(storedEnergy + got, max);
            standbyChargeWattsLast = got / tickFactor;
        }
        /// <summary>
        /// 直接向電網索取電量（Wd），回傳實際取得量。
        /// 有電池的電網從電池抽；沒電池的電網取用這段期間（elapsed tick）的發電盈餘。
        /// </summary>
        private static float DrawFromNet(PowerNet net, float amount, int elapsed)
        {
            List<CompPowerBattery> bats = net.batteryComps;
            if (bats.Count == 0)
            {
                return Mathf.Clamp(net.CurrentEnergyGainRate() * elapsed, 0f, amount);
            }
            float remaining = amount;
            for (int i = 0; i < bats.Count && remaining > 1E-07f; i++)
            {
                float take = Mathf.Min(bats[i].StoredEnergy, remaining);
                if (take <= 0f) continue;
                bats[i].DrawPower(take);
                remaining -= take;
            }
            return amount - remaining;
        }
        private bool GridCanSustainUs()
        {
            PowerNet net = PowerNet;
            if (net == null) return false;
            // 太陽閃焰等停電事件：PowerNet 只會把耗電端關掉，發電端仍回報正輸出，
            // CurrentEnergyGainRate 因此看起來充裕。若不在這裡擋下，自供電→切回電網→被關掉→再自供電
            // 會每 20 tick 反覆一次，砲塔在兩個狀態之間閃爍。
            if (parent.Map.gameConditionManager.ElectricityDisabled(parent.Map)) return false;
            float postSwitchWatts = storedEnergy < BatteryProps.internalBatteryMax
                ? Props.PowerConsumption + BatteryProps.chargeRateWatts
                : Props.PowerConsumption;

            float needPerTick = postSwitchWatts * WattsToWattDaysPerTick;
            if (net.CurrentEnergyGainRate() >= needPerTick)
                return true;
            return net.CurrentStoredEnergy() >= needPerTick * 600f;
        }

        // ── inspect string ─────────────────────────────────────────────────────────
        public override string CompInspectStringExtra()
        {
            string baseStr  = base.CompInspectStringExtra();
            float  maxWd    = BatteryProps.internalBatteryMax;

            string batteryLine = "FFF_InternalBattery".Translate()
                + ": " + storedEnergy.ToString("F0")
                + " / " + maxWd.ToString("F0") + " Wd";

            if (storedEnergy >= maxWd)
                batteryLine += " (" + "FFF_BatteryFull".Translate() + ")";
            else if (Standby)
                batteryLine += " (" + (standbyChargeWattsLast > 0f
                    ? "FFF_StandbyCharging".Translate(standbyChargeWattsLast.ToString("F0"))
                    : "FFF_StandbyIdle".Translate()) + ")";
            else if (selfPowered)
                batteryLine += " (" + "FFF_SelfPowered".Translate() + ")";

            return baseStr.NullOrEmpty()
                ? batteryLine
                : batteryLine + "\n" + baseStr;
        }
    }
}
