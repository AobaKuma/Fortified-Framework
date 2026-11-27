using Fortified;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace AerospaceSample
{
    /// <summary>
    /// 雷射點防禦：實作 <see cref="IAirEffector"/>，生成時向 <see cref="MapComponent_AirEffectors"/> 註冊。
    /// <see cref="UsesFireControl"/> 為 false：不需要火控雷達、不鎖定、不佔火力通道；
    /// 只要本陣營看得到航跡（自己的感測器或同陣營資料鏈）且政策允許，每次掃描都會問它能不能開火。
    /// <see cref="AirEffectorShot.AngularSpeed"/> = 0 表示瞬間命中，當場擲骰，不產生攔截物。
    ///
    /// Laser point defence. Implements <see cref="IAirEffector"/> without fire control: no lock, no fire channel.
    /// AngularSpeed 0 = hit-scan, resolved immediately.
    ///
    /// 同時實作 <see cref="IMapAirEffector"/>：也打地圖層波次（中過程與末端）。兩層共用同一個冷卻。
    /// 地圖層同樣不需要火控；由框架挑最早落地的目標、擲骰、結算並播放攔截特效。
    /// Also implements <see cref="IMapAirEffector"/> so it shoots at map-layer waves; both layers share one cooldown.
    /// </summary>
    public class CompProperties_LaserPointDefense : CompProperties
    {
        /// <summary>世界層射程（地表格）。雷射在世界尺度射程很短，只能保護自己的據點。</summary>
        public float rangeTiles = 1.5f;

        /// <summary>只打這個高度以下（公尺）：大氣衰減。</summary>
        public float maxAltitude = 12000f;

        public float hitChance = 0.35f;
        public int cooldownTicks = 600;

        /// <summary>地圖層（來襲物已進入這張地圖的預警範圍）每發的命中率，再套修正鏈。</summary>
        public float mapHitChance = 0.5f;

        /// <summary>地圖層哪些階段會開火；末端是來襲物快落地的最後幾秒（有 CIWS 時才有末端窗口）。</summary>
        public bool mapMidcourse = true;
        public bool mapTerminal = true;

        public SoundDef fireSound;

        public CompProperties_LaserPointDefense() => compClass = typeof(CompLaserPointDefense);
    }

    public class CompLaserPointDefense : ThingComp, IAirEffector, IMapAirEffector
    {
        private int readyTick;

        public CompProperties_LaserPointDefense Props => (CompProperties_LaserPointDefense)props;

        public Thing Source => parent;
        public bool UsesFireControl => false;
        public int ReadyTick => readyTick;

        /// <summary>有電就算有「彈藥」；冷卻中也回傳 true（防空只在完全不能用時放棄）。</summary>
        public bool Armed => CompAirDefenseDevice.IsOperational(parent);

        public bool CanFire(int now) => Armed && now >= readyTick;

        public bool CanEngage(AirEngagement engagement)
            => engagement.DistanceTiles <= Props.rangeTiles && engagement.Altitude <= Props.maxAltitude;

        public AirEffectorShot PrepareShot(AirEngagement engagement) => new AirEffectorShot
        {
            AngularSpeed = 0f,
            BaseHitChance = Props.hitChance,
        };

        public bool TryFire(AirEngagement engagement, int now, Vector3 meetingPoint)
        {
            if (!CanFire(now)) { return false; }
            readyTick = now + Props.cooldownTicks;
            Props.fireSound?.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            FleckMaker.ThrowLightningGlow(parent.TrueCenter(), parent.Map, 1.5f);
            return true;
        }

        // ---- 地圖層（IMapAirEffector） ----

        public bool CanEngage(MapAirEngagement engagement)
            => (engagement.Phase == MapAirPhase.Midcourse ? Props.mapMidcourse : Props.mapTerminal)
               && engagement.Domain == CombatDomainDefOf.Air && engagement.Altitude <= Props.maxAltitude;

        public float HitChance(MapAirEngagement engagement) => Props.mapHitChance;

        public bool TryFire(MapAirEngagement engagement, int now)
        {
            if (!CanFire(now)) { return false; }
            readyTick = now + Props.cooldownTicks;
            Props.fireSound?.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            FleckMaker.ThrowLightningGlow(parent.TrueCenter(), parent.Map, 1.5f);
            return true;
        }

        // RegisterAll 依實作的介面一次註冊（這裡是世界層與地圖層武器）；直接呼叫 Register(this) 會因為多載而不明確。
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            parent.Map.GetComponent<MapComponent_AirEffectors>()?.RegisterAll(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            map?.GetComponent<MapComponent_AirEffectors>()?.DeregisterAll(this);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref readyTick, "laserReadyTick");
        }

        public override string CompInspectStringExtra()
        {
            int left = readyTick - Find.TickManager.TicksGame;
            return "AerospaceSample.LaserInspect".Translate(Props.rangeTiles.ToString("0.#"), Props.hitChance.ToStringPercent(),
                left > 0 ? left.ToStringTicksToPeriod() : "AerospaceSample.Ready".Translate().ToString());
        }
    }
}
