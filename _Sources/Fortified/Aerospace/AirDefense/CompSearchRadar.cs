using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>階段一：搜索雷達。提供預警並計算攔截窗口。</summary>
    public class CompProperties_SearchRadar : CompProperties
    {
        /// <summary>搜索距離：越大偵測範圍越廣、預警窗口越長。</summary>
        public int searchDistance = 100;

        public float worldDetectionRange = 8f;

        /// <summary>功率等級：放大預警窗口（對應設計圖 Σ(搜索距離 × 功率)）。</summary>
        public int powerLevel = 1;

        /// <summary>反隱身等級：需 >= 目標隱身值才能完整偵測，否則窗口打折。</summary>
        public int antiStealthLevel = 0;

        /// <summary>每「搜索距離」單位換算的窗口 ticks。</summary>
        public float ticksPerDistance = 12f;

        /// <summary>
        /// 是否不受反輻射導引頭（<see cref="GuidanceType_AntiRadiation"/>）的目標選擇影響。
        /// 設為 true 時，此雷達不會被列入反輻射制導的候選目標清單（例如純被動聲學早期預警裝置，
        /// 不發射任何主動電磁波，理論上無法被反輻射導引頭偵測鎖定）。
        /// </summary>
        public bool immuneToAntiRadiationSeeker = false;

        /// <summary>
        /// 解析度等級：目標 distanceLevel 低於此值時，有效距離與預警窗口依比例縮水
        /// （min(1, distanceLevel / resolutionLevel)）。0 = 不限制。長程預警雷達用它表現「看得遠、看不清」。
        /// </summary>
        public float resolutionLevel = 0f;

        public CompProperties_SearchRadar()
        {
            compClass = typeof(CompSearchRadar);
        }
    }

    public class CompSearchRadar : CompAirDefenseDevice, IAirSensor
    {
        public CompProperties_SearchRadar Props => (CompProperties_SearchRadar)props;

        Thing IAirSensor.Source => parent;

        float IAirSensor.WorldDetectionRange(AirTargetExtension target) => EffectiveWorldRange(target);

        /// <summary>
        /// 地圖層預警窗口：搜索距離 × 每單位 ticks × 功率（超線性），乘解析度、功率段位、操作員，
        /// 再依反隱身差距加成或打折。
        /// </summary>
        public float MapWarningTicks(AirTargetExtension tp)
        {
            AirDefenseTuningDef tuning = AirDefenseTuningDef.Current;
            float powerFactor = Mathf.Pow(Mathf.Max(1, Props.powerLevel), tuning.searchPowerWindowExponent);
            float eff = Props.searchDistance * Props.ticksPerDistance * powerFactor;
            eff *= MissileWorldMotion.ResolutionFactor(Props.resolutionLevel, tp)
                * PowerThrottleUtility.WindowFactor(parent) * EWControlUtility.RadarRangeFactor(parent);
            int antiGap = Props.antiStealthLevel - tp.stealthLevel;
            eff *= antiGap < 0 ? tuning.mapStealthWindowFactor : 1f + tuning.mapAntiStealthWindowBonusPerLevel * antiGap;
            return eff;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            MapComponent_AirDefense m = Manager;
            if (m != null) { m.searchRadars.Add(this); }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            MapComponent_AirDefense m = map != null ? map.GetComponent<MapComponent_AirDefense>() : null;
            if (m != null) { m.searchRadars.Remove(this); }
        }

        /// <summary>世界層有效距離：基礎（含反隱身、解析度）× 功率段位 × 操作員智識。</summary>
        public float EffectiveWorldRange(AirTargetExtension target)
            => MissileWorldMotion.DetectionRange(Props.worldDetectionRange, Props.antiStealthLevel, target, Props.resolutionLevel)
               * PowerThrottleUtility.RangeFactor(parent) * EWControlUtility.RadarRangeFactor(parent);

        // 參考目標來自調校 Def（AirDefenseTuningDef.radarRangeReferences），框架不寫死內容 mod 的彈種
        private string EffectiveRangesLine()
        {
            var references = AirDefenseTuningDef.Current.radarRangeReferences;
            if (references.NullOrEmpty()) { return null; }
            var parts = new System.Collections.Generic.List<string>(references.Count);
            foreach (RadarRangeReference reference in references)
            {
                if (reference?.target == null) { continue; }
                float range = EffectiveWorldRange(reference.target.GetModExtension<AirTargetExtension>() ?? AirTargetExtension.Default);
                parts.Add("FFF.BVR.World.EffectiveRangeEntry".Translate(reference.labelKey.Translate(), range.ToString("0.#")));
            }
            return parts.Count == 0 ? null
                : "FFF.BVR.World.EffectiveRanges".Translate(parts.ToCommaList());
        }

        public override string CompInspectStringExtra()
        {
            string ranges = EffectiveRangesLine();
            return "FFF.BVR.World.RadarRange".Translate(Props.worldDetectionRange) + "\n"
                + (ranges != null ? ranges + "\n" : "")
                + "FFF.BVR.SearchRadar".Translate(Props.searchDistance, Props.antiStealthLevel,
                    Active ? "FFF.BVR.Online".Translate() : "FFF.BVR.Offline".Translate());
        }
    }
}
