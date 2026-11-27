using System.Collections.Generic;
using System.Linq;
using Fortified;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 只提供 <see cref="IAirTrack"/> 的外部航跡在防空網路裡的樣子。
    /// 網路需要的偵測與鎖定狀態由 <see cref="WorldComponent_AirDefenseNetwork"/> 依世界物件 ID 保存。
    /// </summary>
    public sealed class ExternalAirTrack : IAirDefenseTrack
    {
        public IAirTrack Inner { get; set; }
        public AirTrackState State { get; }
        private AirTargetExtension props;

        public ExternalAirTrack(IAirTrack inner, AirTrackState state)
        {
            Inner = inner;
            State = state;
        }

        public WorldObject Self => Inner.Self;
        public Faction Attacker => Inner.Attacker;
        public PlanetTile Origin => Inner.Origin;
        public PlanetTile Destination => Inner.Destination;
        public MapParent TargetParent => Inner.TargetParent;
        public MissileConfig Config => Inner.Loadout as MissileConfig;
        public IMissileLoadout Loadout => Inner.Loadout;
        public AirTrackSignature Signature => Inner.Signature;
        public float AngularSpeed => Inner.AngularSpeed;
        public bool VariableSpeed => Inner.VariableSpeed;
        public int TicksToArrival => Inner.TicksToArrival;
        public int RouteRevision => Inner.RouteRevision;
        public float AltitudeAt(int ticksAhead) => Inner.AltitudeAt(ticksAhead);
        public bool ModifiesDefense => Inner.ModifiesDefense;
        public UnityEngine.Vector3 PositionAt(int ticksAhead) => Inner.PositionAt(ticksAhead);
        public void ModifyDefense(AirDefenseQuery query, ref float value) => Inner.ModifyDefense(query, ref value);

        public AirTargetExtension TargetProps
        {
            get
            {
                AirTrackSignature signature = Inner.Signature;
                if (props == null || !props.Signature.SameAs(signature)) { props = AirTargetExtension.Default.WithSignature(signature); }
                return props;
            }
        }

        public void Notify_Intercepted(Faction by)
        {
            WorldObject self = Self;
            if (self == null || self.Destroyed) { return; }
            if (by == Faction.OfPlayer || Attacker == Faction.OfPlayer)
            {
                Messages.Message("FFF.BVR.World.Intercepted".Translate(self.Label, by?.Name ?? "?"), new GlobalTargetInfo(self),
                    by == Faction.OfPlayer ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent, historical: false);
            }
            WorldComponent_AirDefenseNetwork.Current?.AddFlash(AirTrackDisplay.Raised(PositionAt(0), AirTrackDisplay.CurrentLift(Inner)));
            Inner.Notify_Intercepted(by);
        }
    }

    /// <summary>
    /// 地圖層與世界層進入 <see cref="AirDefensePipeline"/> 與 <see cref="AirCombatEvents"/> 的共用入口：
    /// 把 DMSE 的節點、波次目標換成框架看得懂的情境。沒有修正或處理者時完全不配置物件。
    /// </summary>
    public static class AirDefenseHooks
    {
        /// <summary>框架對外只露出原本的航跡，外部 mod 才能用自己的型別判斷。</summary>
        public static IAirTrack Public(IAirTrack track) => track is ExternalAirTrack external ? external.Inner : track;

        /// <summary>飛彈攔截彈的參數：速度與命中係數來自彈體，加成與空爆來自戰鬥部。</summary>
        public static AirEffectorShot ShotFrom(MissileConfig config)
        {
            MissilePartDef warhead = config?.PartFor(MissilePartCategoryDefOf.Warhead);
            float payload = config?.PayloadCapacity ?? 0f;
            return new AirEffectorShot
            {
                AngularSpeed = MissileWorldMotion.AngularSpeed(config) * AirDefenseTuningDef.Current.interceptorSpeedMultiplier,
                HitFactor = config?.InterceptHitFactor ?? 1f,
                HitBonus = warhead?.warheadEffect?.InterceptBonus(payload) ?? 0f,
                ExtraRolls = warhead?.warheadEffect?.ExtraInterceptRolls(payload) ?? 0,
                Loadout = config,
            };
        }

        // ---- 世界層數值 ----

        public static float WorldStat(AirDefenseStat stat, float value, AirDefenseNode node, IAirDefenseTrack track, AirTargetExtension props,
            IMissileLoadout interceptor = null, Thing sensor = null, int eta = 0)
        {
            IMissileLoadout target = track?.Loadout;
            if (!AirDefensePipeline.MayModify(track, target, interceptor)) { return value; }
            return AirDefensePipeline.Evaluate(new AirDefenseQuery
            {
                Stat = stat,
                Layer = AirCombatLayer.World,
                Defender = node?.faction,
                Attacker = track?.Attacker,
                Signature = (track != null ? track.TargetProps : props ?? AirTargetExtension.Default).Signature,
                Site = node?.parent,
                Map = node?.local?.map,
                Track = Public(track),
                TargetObject = track?.Self,
                TargetLoadout = target,
                InterceptorLoadout = interceptor,
                Sensor = sensor,
                EtaTicks = eta,
            }, value);
        }

        // ---- 地圖層數值 ----

        public static float MapStat(AirDefenseStat stat, float value, Map map, Faction defender, Thing payload,
            IMissileLoadout interceptor = null, Thing sensor = null, int eta = 0, bool terminal = false)
        {
            IMissileLoadout target = (payload as IIncomingMissile)?.Config;
            if (!AirDefensePipeline.MayModify(null, target, interceptor)) { return value; }
            return AirDefensePipeline.Evaluate(new AirDefenseQuery
            {
                Stat = stat,
                Layer = AirCombatLayer.Map,
                Defender = defender,
                Attacker = AttackerOf(payload),
                Signature = SignatureOf(payload),
                Site = map?.Parent,
                Map = map,
                TargetThing = payload,
                TargetLoadout = target,
                InterceptorLoadout = interceptor,
                Sensor = sensor,
                EtaTicks = eta,
                Terminal = terminal,
            }, value);
        }

        private static AirTrackSignature SignatureOf(Thing payload)
            => (payload?.def?.GetModExtension<AirTargetExtension>() ?? AirTargetExtension.Default).Signature;

        /// <summary>來襲物的陣營：導彈看發射方；空投艙看艙體、內容物與乘員。</summary>
        public static Faction AttackerOf(Thing payload)
        {
            if (payload is IIncomingMissile missile) { return missile.Attacker; }
            if (payload == null) { return null; }
            if (payload.Faction != null) { return payload.Faction; }
            if (payload is Skyfaller faller && faller.innerContainer.Any)
            {
                Thing inner = faller.innerContainer.First();
                return inner.Faction ?? AirDefenseDetectionUtility.PawnsIn(inner).FirstOrDefault()?.Faction;
            }
            return null;
        }

        // ---- 事件 ----

        public static void WorldEvent(AirCombatEventKind kind, IAirDefenseTrack track, Faction defender, MapParent site, Map map = null,
            IMissileLoadout interceptor = null, Thing effector = null, float hitChance = 0f,
            AirInterceptOutcome outcome = AirInterceptOutcome.None, bool secondary = false, bool removed = false)
        {
            if (!AirCombatEvents.HasListeners) { return; }
            AirCombatEvents.Raise(BuildWorldEvent(kind, track, defender, site, map, interceptor, effector, hitChance, outcome, secondary, removed));
        }

        /// <summary>世界層動作之前問否決者（<see cref="AirCombatVeto"/>）；沒有否決者時直接允許，不建立事件。</summary>
        public static bool WorldAllows(AirCombatEventKind kind, IAirDefenseTrack track, Faction defender, MapParent site, Map map = null,
            IMissileLoadout interceptor = null, Thing effector = null, float hitChance = 0f)
            => !AirCombatEvents.HasVetoes
               || AirCombatEvents.Allows(BuildWorldEvent(kind, track, defender, site, map, interceptor, effector, hitChance));

        /// <summary>地圖層動作之前問否決者；沒有否決者時直接允許。</summary>
        public static bool MapAllows(AirCombatEventKind kind, Map map, Faction defender, Thing payload,
            IMissileLoadout interceptor = null, Thing effector = null, float hitChance = 0f, bool terminal = false)
            => !AirCombatEvents.HasVetoes
               || AirCombatEvents.Allows(BuildMapEvent(kind, map, defender, payload, interceptor, effector, hitChance, terminal: terminal));

        private static AirCombatEvent BuildWorldEvent(AirCombatEventKind kind, IAirDefenseTrack track, Faction defender, MapParent site, Map map,
            IMissileLoadout interceptor, Thing effector, float hitChance,
            AirInterceptOutcome outcome = AirInterceptOutcome.None, bool secondary = false, bool removed = false)
        {
            return new AirCombatEvent
            {
                Kind = kind,
                Layer = AirCombatLayer.World,
                Defender = defender,
                Attacker = track?.Attacker,
                Site = site,
                Map = map,
                Track = Public(track),
                TargetObject = track?.Self,
                Signature = track?.TargetProps.Signature ?? default,
                TargetLoadout = track?.Loadout,
                InterceptorLoadout = interceptor,
                Effector = effector,
                HitChance = hitChance,
                Outcome = outcome,
                Secondary = secondary,
                Removed = removed,
            };
        }

        /// <summary>封鎖攔截的事件：目標是沒有航跡的載具。</summary>
        public static void BlockadeEvent(AirCombatEventKind kind, WorldObject flyer, Faction defender, MapParent site,
            float hitChance, AirInterceptOutcome outcome = AirInterceptOutcome.None)
        {
            if (!AirCombatEvents.HasListeners) { return; }
            AirCombatEvents.Raise(new AirCombatEvent
            {
                Kind = kind,
                Layer = AirCombatLayer.World,
                Defender = defender,
                Attacker = flyer?.Faction,
                Site = site,
                TargetObject = flyer,
                Signature = AirTargetExtension.Default.Signature,
                HitChance = hitChance,
                Outcome = outcome,
            });
        }

        /// <summary>
        /// 建立地圖層事件；沒有處理者時回傳 null（<see cref="AirCombatEvents.Raise"/> 接受 null）。
        /// 命中會清掉來襲物，所以要在移除目標前建立，移除後才發出。
        /// </summary>
        public static AirCombatEvent MapEvent(AirCombatEventKind kind, Map map, Faction defender, Thing payload,
            IMissileLoadout interceptor = null, Thing effector = null, float hitChance = 0f,
            AirInterceptOutcome outcome = AirInterceptOutcome.None, bool secondary = false, bool terminal = false)
        {
            if (!AirCombatEvents.HasListeners) { return null; }
            return BuildMapEvent(kind, map, defender, payload, interceptor, effector, hitChance, outcome, secondary, terminal);
        }

        private static AirCombatEvent BuildMapEvent(AirCombatEventKind kind, Map map, Faction defender, Thing payload,
            IMissileLoadout interceptor = null, Thing effector = null, float hitChance = 0f,
            AirInterceptOutcome outcome = AirInterceptOutcome.None, bool secondary = false, bool terminal = false)
        {
            return new AirCombatEvent
            {
                Kind = kind,
                Layer = AirCombatLayer.Map,
                Defender = defender,
                Attacker = AttackerOf(payload),
                Site = map?.Parent,
                Map = map,
                TargetThing = payload,
                Signature = SignatureOf(payload),
                TargetLoadout = (payload as IIncomingMissile)?.Config,
                InterceptorLoadout = interceptor,
                Effector = effector,
                HitChance = hitChance,
                Outcome = outcome,
                Secondary = secondary,
                Terminal = terminal,
            };
        }
    }
}
