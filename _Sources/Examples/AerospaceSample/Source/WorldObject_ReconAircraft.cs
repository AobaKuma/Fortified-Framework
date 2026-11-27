using Fortified;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace AerospaceSample
{
    /// <summary>
    /// 偵察機：實作 <see cref="IAirTrack"/> 的世界物件。加入世界後，防空網路會自動把它當成航跡：
    /// 偵測、鎖定、發射攔截彈、命中時呼叫 <see cref="Notify_Intercepted"/>。偵測與鎖定狀態由 FFF 依物件 ID 保存，這裡不必存。
    /// 抵達時呼叫 <see cref="WorldComponent_AirDefenseNetwork.ReportArrival"/>，事件處理者與 NPC 警戒才會收到。
    ///
    /// A world object that implements <see cref="IAirTrack"/>; FFF's air defence picks it up automatically.
    /// </summary>
    public class WorldObject_ReconAircraft : WorldObject, IAirTrack
    {
        public PlanetTile origin = PlanetTile.Invalid;
        public PlanetTile destination = PlanetTile.Invalid;
        public int departTick;
        public int arrivalTick;

        private AircraftExtension Ext => def.GetModExtension<AircraftExtension>() ?? AircraftExtension.Default;

        public WorldObject Self => this;
        public Faction Attacker => Faction;
        public PlanetTile Origin => origin;
        public PlanetTile Destination => destination;

        public AirTrackSignature Signature => new AirTrackSignature
        {
            distanceLevel = Ext.distanceLevel,
            stealthLevel = Ext.stealthLevel,
            speed = Ext.speed,
            altitude = Ext.altitude,
        };

        private Vector3 Start => Find.WorldGrid.GetTileCenter(origin);
        private Vector3 End => Find.WorldGrid.GetTileCenter(destination);

        public float AngularSpeed => arrivalTick <= departTick ? 0f
            : GenMath.SphericalDistance(Start.normalized, End.normalized) / (arrivalTick - departTick);

        public int TicksToArrival => Mathf.Max(0, arrivalTick - Find.TickManager.TicksGame);

        public Vector3 PositionAt(int ticksAhead)
        {
            float pct = Mathf.Clamp01((Find.TickManager.TicksGame + Mathf.Max(0, ticksAhead) - departTick) / (float)Mathf.Max(1, arrivalTick - departTick));
            return Vector3.Slerp(Start, End, pct);
        }

        public override Vector3 DrawPos => AirTrackDisplay.Raised(PositionAt(0), AirTrackDisplay.CurrentLift(this));

        /// <summary>依飛行距離與 <see cref="AircraftExtension.tilesPerDay"/> 設定起降時間。</summary>
        public void Launch(PlanetTile from, PlanetTile to)
        {
            origin = from;
            destination = to;
            Tile = from;
            departTick = Find.TickManager.TicksGame;
            float tiles = Find.WorldGrid.ApproxDistanceInTiles(from, to);
            arrivalTick = departTick + Mathf.Max(600, Mathf.RoundToInt(tiles / Mathf.Max(0.1f, Ext.tilesPerDay) * GenDate.TicksPerDay));
        }

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (Find.TickManager.TicksGame < arrivalTick) { return; }
            MapParent site = WorldComponent_AirDefenseNetwork.ReportArrival(this);
            Messages.Message("AerospaceSample.AircraftArrived".Translate(LabelCap, site?.LabelCap ?? destination.ToString()),
                new GlobalTargetInfo(destination), MessageTypeDefOf.NeutralEvent, historical: false);
            Destroy();
        }

        public void Notify_Intercepted(Faction by)
        {
            if (Destroyed) { return; }
            Messages.Message("AerospaceSample.AircraftShotDown".Translate(LabelCap, by?.Name ?? "?"),
                new GlobalTargetInfo(destination), MessageTypeDefOf.NeutralEvent, historical: false);
            WorldComponent_AirDefenseNetwork.Current?.AddFlash(DrawPos);
            Destroy();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref origin, "origin", PlanetTile.Invalid);
            Scribe_Values.Look(ref destination, "destination", PlanetTile.Invalid);
            Scribe_Values.Look(ref departTick, "departTick");
            Scribe_Values.Look(ref arrivalTick, "arrivalTick");
        }
    }

    /// <summary>飛機的特徵，放在 WorldObjectDef 的 modExtensions。</summary>
    public class AircraftExtension : DefModExtension
    {
        public float distanceLevel = 1.2f;
        public int stealthLevel;
        public float speed = 12f;
        public float altitude = 8000f;
        public float tilesPerDay = 40f;

        public static readonly AircraftExtension Default = new AircraftExtension();
    }
}
