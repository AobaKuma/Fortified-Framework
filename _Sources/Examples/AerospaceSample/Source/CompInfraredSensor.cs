using Fortified;
using RimWorld;
using Verse;

namespace AerospaceSample
{
    /// <summary>
    /// 被動紅外線搜索感測器：實作 <see cref="IAirSensor"/>，生成時向 <see cref="MapComponent_AirEffectors"/> 註冊。
    /// 註冊後這張地圖就有本陣營的防空節點；世界層偵測取所有感測器的最大半徑，地圖層把預警窗口相加。
    /// 紅外線看的是熱：隱身塗層沒用，但低空、慢速的目標熱訊號小、看得近。
    ///
    /// Passive infrared sensor. Implements <see cref="IAirSensor"/> and registers with
    /// <see cref="MapComponent_AirEffectors"/> on spawn, which creates an air-defence node for its faction on this map.
    /// </summary>
    public class CompProperties_InfraredSensor : CompProperties
    {
        /// <summary>對標準目標（速度 15、高度 1000 m）的世界層探測半徑（地表格）。</summary>
        public float worldRange = 5f;

        /// <summary>地圖層每具感測器貢獻的預警窗口（ticks）。</summary>
        public float mapWarningTicks = 600f;

        /// <summary>目標速度相對標準值的影響：快的目標氣動加熱，熱訊號大。</summary>
        public float speedExponent = 0.5f;

        public CompProperties_InfraredSensor() => compClass = typeof(CompInfraredSensor);
    }

    public class CompInfraredSensor : ThingComp, IAirSensor
    {
        public CompProperties_InfraredSensor Props => (CompProperties_InfraredSensor)props;

        public Thing Source => parent;

        // 與出貨雷達同一套規則：生成、損壞、開關、電力、擊暈、操作員。
        public bool Active => CompAirDefenseDevice.IsOperational(parent);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            parent.Map.GetComponent<MapComponent_AirEffectors>()?.Register(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            map?.GetComponent<MapComponent_AirEffectors>()?.Deregister(this);
        }

        private float HeatFactor(AirTargetExtension target)
        {
            float speed = target.speed / AirTargetExtension.Default.speed;
            // 高度 1000 m 為中性；貼地飛行有地形遮蔽，看得比較近。
            float altitude = target.altitude < 300f ? 0.6f : 1f;
            return UnityEngine.Mathf.Pow(UnityEngine.Mathf.Max(0.1f, speed), Props.speedExponent) * altitude;
        }

        public float WorldDetectionRange(AirTargetExtension target) => Props.worldRange * HeatFactor(target);

        public float MapWarningTicks(AirTargetExtension target) => Props.mapWarningTicks * HeatFactor(target);

        public override string CompInspectStringExtra()
            => "AerospaceSample.IRSensorInspect".Translate(Props.worldRange.ToString("0.#"),
                (Active ? "AerospaceSample.Online" : "AerospaceSample.Offline").Translate());
    }
}
