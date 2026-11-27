using Fortified;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 防空封鎖（2026-10-31 起主動攔截；2026-11-27 併入一般接戰）：每 60 ticks 找出飛進封鎖圈、不被允許通行的載具
    /// （運輸艙與穿梭機、重力船，以及註冊的 <see cref="IBlockadeTargetHandler"/>，例如飛行中的轉移艦），
    /// 把它們包成航跡（<see cref="BlockadeViolatorTrack"/>）交給 <see cref="WorldComponent_AirDefenseNetwork"/>。
    /// 之後的偵測、鎖定、火力通道、武器選擇（含點防禦式武器）、攔截彈與結算都走一般流程，事件也與飛彈相同；
    /// 只有封鎖據點會接戰它們（<see cref="AirDefenseNode"/> 依 <see cref="BlockadeViolatorTrack.IsZoneOf"/> 判斷）。
    /// 類別名稱不變：存檔裡有這個元件。
    /// </summary>
    [FFF_Feature(FFF_AerospaceFeatures.Missiles)]
    public class WorldComponent_BlockadeInterception : WorldComponent
    {
        private const int ScanIntervalTicks = 60;

        private static readonly BlockadeTrackProvider provider = new BlockadeTrackProvider();

        private readonly Dictionary<int, BlockadeViolatorTrack> violators = new Dictionary<int, BlockadeViolatorTrack>();
        private readonly List<IAirTrack> current = new List<IAirTrack>();
        private readonly HashSet<int> seen = new HashSet<int>();

        public static WorldComponent_BlockadeInterception Current => Find.World?.GetComponent<WorldComponent_BlockadeInterception>();

        /// <summary>目前違規、會被封鎖據點接戰的載具。</summary>
        public IReadOnlyList<IAirTrack> Violators => current;

        public WorldComponent_BlockadeInterception(World world) : base(world)
        {
            AirDefenseExtensions.AddProvider(provider);
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            int now = Find.TickManager.TicksGame;
            if (now % ScanIntervalTicks != 0) { return; }
            Refresh(now);
        }

        private void Refresh(int now)
        {
            List<BlockadeZone> zones = AirspaceRestrictionUtility.ActiveZones;
            if (zones.Count == 0 && violators.Count == 0) { return; }
            WorldComponent_AirDefenseNetwork network = WorldComponent_AirDefenseNetwork.Current;
            seen.Clear();
            List<WorldObject> objects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < objects.Count && zones.Count > 0; i++)
            {
                WorldObject flyer = objects[i];
                if (!BlockadeHitUtility.InFlight(flyer) || flyer.Faction == null) { continue; }
                Vector3 position = flyer.DrawPos;
                List<BlockadeZone> violated = null;
                foreach (BlockadeZone zone in zones)
                {
                    if (zone.tile.Layer != flyer.Tile.Layer || AirspaceRestrictionUtility.AllowedThrough(zone, flyer.Faction)) { continue; }
                    if (MissileWorldUtility.SurfaceTilesBetween(zone.center, position) > zone.radius) { continue; }
                    (violated ??= new List<BlockadeZone>()).Add(zone);
                }
                if (violated == null) { continue; }
                seen.Add(flyer.ID);
                bool added = !violators.TryGetValue(flyer.ID, out BlockadeViolatorTrack track);
                if (added)
                {
                    track = new BlockadeViolatorTrack(flyer);
                    violators[flyer.ID] = track;
                }
                track.Update(violated, position, now);
                // 新的違規者：下一個掃描 tick 啟動封鎖據點的節點並開始掃描（不必等長週期檢查）。
                if (added) { network?.NotifyTrackChanged(flyer); }
            }
            if (violators.Count != seen.Count)
            {
                foreach (int id in violators.Keys.Where(k => !seen.Contains(k)).ToList()) { violators.Remove(id); }
            }
            current.Clear();
            current.AddRange(violators.Values);
        }

        /// <summary>把目前的違規者交給防空網路（航跡來源）。</summary>
        private sealed class BlockadeTrackProvider : AirTrackProvider
        {
            public override void CollectTracks(List<IAirTrack> into)
            {
                IReadOnlyList<IAirTrack> list = Current?.Violators;
                if (list == null) { return; }
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Self != null && !list[i].Self.Destroyed) { into.Add(list[i]); }
                }
            }
        }
    }

    /// <summary>
    /// 封鎖圈裡的違規載具，包成 <see cref="IAirTrack"/>。載具本身不公開未來位置（原版的航線欄位是私有的），
    /// 所以每 60 ticks 取樣一次位置，沿大圓外推：運輸艙、重力船與轉移艦都是等速大圓航線，外推是準的。
    /// <see cref="TicksToArrival"/> 是預計飛出封鎖圈的時間（上限 <see cref="AirDefenseAlertTuningDef.blockadeEngageWindowTicks"/>）：
    /// 攔截解必須在它飛出去之前會合，命中率的導引時間也用它。
    /// </summary>
    public sealed class BlockadeViolatorTrack : IAirTrack
    {
        private const int ExitStepTicks = 60;

        private readonly WorldObject flyer;
        private List<BlockadeZone> zones = new List<BlockadeZone>();
        private Vector3 lastSample, prevSample;
        private int lastTick = -1, prevTick = -1;

        public BlockadeViolatorTrack(WorldObject flyer) { this.flyer = flyer; }

        public WorldObject Self => flyer;
        public Faction Attacker => flyer.Faction;
        public PlanetTile Origin => flyer.Tile;

        /// <summary>沒有目的地：防空依位置接戰，不看目的地。</summary>
        public PlanetTile Destination => PlanetTile.Invalid;

        public IReadOnlyList<BlockadeZone> Zones => zones;

        /// <summary>中性特徵（與改版前的封鎖攔截相同）：不因速度、隱身或高度修正命中率。</summary>
        public AirTrackSignature Signature => new AirTrackSignature
        {
            distanceLevel = 1f,
            stealthLevel = 0,
            speed = AirTargetExtension.Default.speed,
            altitude = AirTargetExtension.Default.altitude,
        };

        public bool VariableSpeed => true;

        /// <summary>兩次取樣之間的角速度（弧度／tick）；只有一次取樣時為 0（當成靜止）。</summary>
        public float AngularSpeed => prevTick < 0 || lastTick <= prevTick ? 0f
            : GenMath.SphericalDistance(prevSample.normalized, lastSample.normalized) / (lastTick - prevTick);

        public int TicksToArrival
        {
            get
            {
                int cap = Mathf.Max(ExitStepTicks, AirDefenseAlertTuningDef.Current.blockadeEngageWindowTicks);
                if (AngularSpeed <= 0f) { return cap; }
                for (int t = ExitStepTicks; t < cap; t += ExitStepTicks)
                {
                    if (!InsideAnyZone(PositionAt(t))) { return t; }
                }
                return cap;
            }
        }

        /// <summary>這個據點與陣營是否就是被違反的封鎖圈。</summary>
        public bool IsZoneOf(MapParent site, Faction faction)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].site == site && zones[i].faction == faction) { return true; }
            }
            return false;
        }

        private bool InsideAnyZone(Vector3 position)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                if (MissileWorldUtility.SurfaceTilesBetween(zones[i].center, position) <= zones[i].radius) { return true; }
            }
            return false;
        }

        internal void Update(List<BlockadeZone> violated, Vector3 position, int now)
        {
            zones = violated;
            if (now == lastTick) { return; }
            prevSample = lastSample;
            prevTick = lastTick;
            lastSample = position;
            lastTick = now;
        }

        /// <summary>從載具目前的位置沿取樣得到的大圓方向外推。</summary>
        public Vector3 PositionAt(int ticksAhead)
        {
            Vector3 here = flyer.DrawPos;
            float radius = MissileWorldUtility.TileCenter(flyer.Tile).magnitude;
            if (radius <= 0f) { radius = here.magnitude; }
            here = here.normalized * radius;
            float speed = AngularSpeed;
            if (ticksAhead <= 0 || speed <= 0f) { return here; }
            Vector3 axis = Vector3.Cross(prevSample.normalized, lastSample.normalized);
            if (axis.sqrMagnitude < 1e-12f) { return here; }
            return Quaternion.AngleAxis(speed * ticksAhead * Mathf.Rad2Deg, axis.normalized) * here;
        }

        public void Notify_Intercepted(Faction by) => BlockadeHitUtility.ApplyHit(flyer, by);

        /// <summary>防空網路對它發射時呼叫：玩家相關時發訊息（與改版前相同）。</summary>
        internal void NotifyEngaged(AirDefenseNode node)
        {
            if (node?.faction == null || flyer.Faction == null) { return; }
            if (flyer.Faction.IsPlayer || node.faction.IsPlayer)
            {
                Messages.Message("FFF.Blockade.Engaging".Translate(node.faction.NameColored, node.parent.LabelCap, flyer.LabelCap),
                    new GlobalTargetInfo(flyer), MessageTypeDefOf.ThreatSmall);
            }
        }

        /// <summary>航跡（可能是防空網路的包裝）背後的封鎖違規者；不是時回傳 null。</summary>
        public static BlockadeViolatorTrack Of(IAirTrack track) => AirDefenseHooks.Public(track) as BlockadeViolatorTrack;
    }

    /// <summary>
    /// 舊版封鎖攔截彈：從據點追向載具目前的位置，到時擲骰命中。2026-11-27 起封鎖改走一般接戰（<see cref="WorldObject_Interceptor"/>），
    /// 不再產生這個物件；保留類別與 Def 只為了讓舊存檔裡還在飛的那一發照樣結算。
    /// </summary>
    public class WorldObject_BlockadeInterceptor : WorldObject
    {
        public int launchTick, interceptTick;
        public Vector3 start, lastTargetPos;
        public WorldObject target;
        public MapParent launchNode;
        public float hitChance;

        private bool TargetAlive => target != null && !target.Destroyed && BlockadeHitUtility.InFlight(target);

        private Vector3 TargetPos => TargetAlive ? target.DrawPos : lastTargetPos;

        public override Vector3 DrawPos => Vector3.Slerp(start, TargetPos, Mathf.Clamp01(
            (float)(Find.TickManager.TicksGame - launchTick) / Mathf.Max(1, interceptTick - launchTick)));

        public override bool ExpandingIconFlipHorizontal
            => GenWorldUI.WorldToUIPosition(start).x > GenWorldUI.WorldToUIPosition(TargetPos).x;

        public override float ExpandingIconRotation
        {
            get
            {
                Vector2 a = GenWorldUI.WorldToUIPosition(start);
                Vector2 b = GenWorldUI.WorldToUIPosition(TargetPos);
                return Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg + 90f;
            }
        }

        public override string GetInspectString() => "FFF.BVR.World.InterceptorInspect".Translate(
            target?.Label ?? "?", Mathf.Max(0, interceptTick - Find.TickManager.TicksGame).ToStringTicksToPeriod());

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (TargetAlive) { lastTargetPos = target.DrawPos; }
            if (Find.TickManager.TicksGame >= interceptTick) { Resolve(); }
        }

        private void Resolve()
        {
            bool notify = Faction == Faction.OfPlayer || target?.Faction == Faction.OfPlayer;
            AirInterceptOutcome outcome;
            if (!TargetAlive)
            {
                outcome = AirInterceptOutcome.Evaded;
                if (notify && target != null && !target.Destroyed)
                {
                    Messages.Message("FFF.BVR.World.Evaded".Translate(target.Label), new GlobalTargetInfo(target), MessageTypeDefOf.NeutralEvent, historical: false);
                }
            }
            // 結算前重查友軍身分：飛行中結盟就安全解除。
            else if (AirDefenseAlertPolicy.IsFriendly(Faction, target.Faction))
            {
                outcome = AirInterceptOutcome.Safed;
                if (notify)
                {
                    Messages.Message("FFF.AirAlert.InterceptorSafed".Translate(target.Label), new GlobalTargetInfo(target), MessageTypeDefOf.NeutralEvent, historical: false);
                }
            }
            else if (Rand.Chance(hitChance))
            {
                outcome = AirInterceptOutcome.Hit;
                WorldComponent_AirDefenseNetwork.Current?.AddFlash(lastTargetPos);
                BlockadeHitUtility.ApplyHit(target, Faction);
            }
            else
            {
                outcome = AirInterceptOutcome.Miss;
                if (notify)
                {
                    Messages.Message("FFF.BVR.World.Missed".Translate(target.Label), new GlobalTargetInfo(target), MessageTypeDefOf.NeutralEvent, historical: false);
                }
            }
            AirDefenseHooks.BlockadeEvent(AirCombatEventKind.InterceptResolved, target, Faction, launchNode, hitChance, outcome);
            Destroy();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref launchTick, "launchTick");
            Scribe_Values.Look(ref interceptTick, "interceptTick");
            Scribe_Values.Look(ref start, "start");
            Scribe_Values.Look(ref lastTargetPos, "lastTargetPos");
            Scribe_Values.Look(ref hitChance, "hitChance");
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // 目標或據點可能已先消失：不寫入指向未存檔世界物件的參照。
                WorldObject liveTarget = target != null && !target.Destroyed ? target : null;
                MapParent liveNode = launchNode != null && !launchNode.Destroyed ? launchNode : null;
                Scribe_References.Look(ref liveTarget, "target");
                Scribe_References.Look(ref liveNode, "launchNode");
            }
            else
            {
                Scribe_References.Look(ref target, "target");
                Scribe_References.Look(ref launchNode, "launchNode");
            }
        }
    }

    /// <summary>
    /// 原版以外、會被封鎖攔截的世界物件（例：轉移艦，由 <see cref="TransferAirDefenseIntegration"/> 註冊）。
    /// 封鎖攔截本身不認識這些型別。
    /// </summary>
    public interface IBlockadeTargetHandler
    {
        /// <summary>這個世界物件是否是自己負責、而且正在飛行（可被攔截）。</summary>
        bool InFlight(WorldObject target);

        /// <summary>是自己負責的物件時套用命中效果並回傳 true。</summary>
        bool TryApplyHit(WorldObject target, Faction by);
    }

    /// <summary>封鎖攔截命中各類載具的效果。</summary>
    public static class BlockadeHitUtility
    {
        private static readonly AccessTools.FieldRef<TravellingTransporters, List<ActiveTransporterInfo>> TransportersOf =
            AccessTools.FieldRefAccess<TravellingTransporters, List<ActiveTransporterInfo>>("transporters");

        private static readonly List<IBlockadeTargetHandler> extraTargets = new List<IBlockadeTargetHandler>();

        /// <summary>加入原版以外的攔截對象；同一個處理器只會加一次。</summary>
        public static void RegisterTarget(IBlockadeTargetHandler handler)
        {
            if (handler != null && !extraTargets.Contains(handler))
            {
                extraTargets.Add(handler);
            }
        }

        /// <summary>會被封鎖攔截的飛行中載具：運輸艙／穿梭機、重力船，以及註冊的其他物件（轉移艦）。</summary>
        public static bool InFlight(WorldObject o)
        {
            if (o == null || o.Destroyed) { return false; }
            if (o is TravellingTransporters || o is Gravship) { return true; }
            for (int i = 0; i < extraTargets.Count; i++)
            {
                if (extraTargets[i].InFlight(o)) { return true; }
            }
            return false;
        }

        public static void ApplyHit(WorldObject target, Faction by)
        {
            switch (target)
            {
                case TravellingTransporters transporters:
                    DownOnePod(transporters, by);
                    return;
                case Gravship gravship:
                    DamageGravship(gravship, by);
                    return;
            }
            for (int i = 0; i < extraTargets.Count; i++)
            {
                if (extraTargets[i].TryApplyHit(target, by)) { return; }
            }
        }

        /// <summary>擊落一艘運輸艙／穿梭機：乘員依 <see cref="InterceptedPawnUtility"/> 處理，其餘內容物與艙體一起銷毀。</summary>
        private static void DownOnePod(TravellingTransporters transporters, Faction by)
        {
            List<ActiveTransporterInfo> pods = TransportersOf(transporters);
            if (pods.NullOrEmpty()) { return; }
            ActiveTransporterInfo pod = pods.RandomElement();
            string label = transporters.Label;
            // 載具可能在這次命中後消失：信件與訊息指向它當下的格子。
            GlobalTargetInfo where = new GlobalTargetInfo(transporters.Tile);
            InterceptedPawnUtility.Resolve(pod.innerContainer.OfType<Pawn>(), new LookTargets(where));
            pod.innerContainer.ClearAndDestroyContents();
            pods.Remove(pod);
            if (transporters.Faction == Faction.OfPlayer || by == Faction.OfPlayer)
            {
                Messages.Message("FFF.Blockade.PodDown".Translate(label, by.NameColored, pods.Count), where, MessageTypeDefOf.NegativeEvent);
            }
            if (pods.Count == 0) { transporters.Destroy(); }
        }

        /// <summary>重力船：船上幾座建築受損（至少保留 1 點耐久，不在飛行中毀掉，避免落地時生成已毀物件）。</summary>
        private static void DamageGravship(Gravship gravship, Faction by)
        {
            List<Thing> buildings = gravship.Things.Where(t => t is Building && t.def.useHitPoints && !t.Destroyed).ToList();
            int damaged = 0;
            foreach (Thing thing in buildings.InRandomOrder().Take(4))
            {
                int loss = Mathf.CeilToInt(thing.MaxHitPoints * Rand.Range(0.25f, 0.5f));
                thing.HitPoints = Mathf.Max(1, thing.HitPoints - loss);
                damaged++;
            }
            Messages.Message("FFF.Blockade.GravshipHit".Translate(gravship.LabelCap, by.NameColored, damaged),
                new GlobalTargetInfo(gravship), MessageTypeDefOf.NegativeEvent);
        }
    }
}
