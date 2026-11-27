using System.Collections.Generic;
using System.Linq;
using Fortified;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>World interception scheduler. Durable state belongs to tracks/interceptors, never this cache.</summary>
    [FFF_Feature(FFF_AerospaceFeatures.Missiles)]
    public class WorldComponent_AirDefenseNetwork : WorldComponent
    {
        public static WorldComponent_AirDefenseNetwork Current => Find.World?.GetComponent<WorldComponent_AirDefenseNetwork>();
        private readonly List<AirDefenseNode> nodes = new List<AirDefenseNode>();
        private readonly List<AirDefenseNode> autonomousNodes = new List<AirDefenseNode>();
        private readonly List<WorldObject_Interceptor> pending = new List<WorldObject_Interceptor>();
        private readonly AirDefenseWorldDrawer drawer = new AirDefenseWorldDrawer();
        private bool rebuild = true;

        // ---- 掃描的活躍期（效能）----
        // 世界上沒有航跡時完全不掃描。每 PresenceCheckIntervalTicks 檢查一次有沒有航跡；有的話整個網路保持掃描到下一次檢查，
        // 而且只有「航線在這段時間內會經過其探測範圍」的 NPC 抽象節點參與（scanNodes）。地圖上的實體雷達節點一律參與。
        // 航跡加入世界或改靶時不必等下一次（NotifyTrackChanged）：只替「那一條」航跡啟動它航線上的節點，而且等到下一個掃描 tick
        // 才一起處理（反正掃描之前節點活不活躍沒有差別），所以齊射時同一個掃描週期內的多枚只付一次固定成本，
        // 也不會每枚都重看全部航跡、重建節點清單。這些都不存檔：讀檔後第一 tick 做一次完整檢查。
        public const int PresenceCheckIntervalTicks = GenTicks.TickLongInterval;
        /// <summary>節點探測半徑之外再多算的距離（格）：預測位置與實際航線的誤差。</summary>
        public const float ActivationPaddingTiles = 2f;
        /// <summary>
        /// 介面與封鎖圈讀到的完整節點清單最多可以舊這麼久（和以前每個掃描週期重建一次的新鮮度相同，但只在有人讀的時候才重建）；
        /// 階段改變或據點加入世界時由 <see cref="MarkNodesDirty"/> 立刻作廢。
        /// </summary>
        private static int NodeListMaxAgeTicks => Mathf.Max(1, AirDefenseTuningDef.Current.scanIntervalTicks);
        private readonly List<AirDefenseNode> scanNodes = new List<AirDefenseNode>();
        private readonly Dictionary<int, int> nodeActiveUntil = new Dictionary<int, int>();
        private readonly List<Vector3> pathScratch = new List<Vector3>();
        private int scanActiveUntil = -1;
        private bool presenceCheckDue = true;
        private bool nodesDirty = true;
        private readonly List<WorldObject> pendingTracks = new List<WorldObject>();

        /// <summary>
        /// 這個長週期內已經判定過的航線。齊射的後續幾枚（同起點、同目的地、同一種目標特性、從差不多的位置出發、
        /// 這段時間內飛得不比它遠）會啟動的節點完全相同，直接略過，連「不在航線上的節點」也不必再算一次。
        /// </summary>
        private struct EvaluatedRoute
        {
            public PlanetTile origin, destination;
            public AirTrackSignature signature;
            public Vector3 start;
            public float reachTiles;
            public int until;
        }
        private readonly List<EvaluatedRoute> evaluatedRoutes = new List<EvaluatedRoute>();

        /// <summary>不知道是哪一條航跡變了（例如 <see cref="AirTrackProvider"/> 開始提供新物件）：下一 tick 做一次完整檢查。</summary>
        public void NotifyTrackChanged() => presenceCheckDue = true;

        /// <summary>這條航跡剛加入世界或改了目的地：下一個掃描 tick 啟動它航線上的節點。</summary>
        public void NotifyTrackChanged(WorldObject track)
        {
            if (track != null && !pendingTracks.Contains(track)) { pendingTracks.Add(track); }
        }

        /// <summary>節點的來源（階段、關係、設備）變了：下一次讀 <see cref="Nodes"/> 時重建。</summary>
        public void MarkNodesDirty() => nodesDirty = true;

        /// <summary>目前是否在掃描（有航跡在飛）。</summary>
        public bool ScanActive => Find.TickManager.TicksGame <= scanActiveUntil;

        /// <summary>這個據點的抽象節點目前是否參與掃描（測試與開發工具用）。</summary>
        public bool NodeScanActive(MapParent parent)
            => parent != null && nodeActiveUntil.TryGetValue(parent.ID, out int until) && until >= Find.TickManager.TicksGame;

        // ---- 移動目標登錄（反艦）----
        private const int MotionSampleIntervalTicks = 60;
        private const int MotionMemoryTicks = 900;
        private struct MotionSample
        {
            public PlanetTile tile;
            public int tick;
            public int lastSeen;
            public float tilesPerTick;
        }
        private readonly Dictionary<int, MotionSample> motion = new Dictionary<int, MotionSample>();
        private int lastMotionSampleTick = -1;
        public bool ShowAllCoverage
        {
            get => FFF_AerospaceMod.Settings?.showBVRWorldCoverage ?? false;
            set
            {
                if (FFF_AerospaceMod.Settings == null) return;
                FFF_AerospaceMod.Settings.showBVRWorldCoverage = value;
                FFF_AerospaceMod.Settings.Write();
            }
        }
        public WorldComponent_AirDefenseNetwork(World world) : base(world) { }

        // ---- 航跡來源 ----
        // 內部航跡（飛彈、空投幽靈）自己保存狀態。外部航跡只實作 IAirTrack，或由 AirTrackProvider 提供；
        // 它們的狀態存在這裡，包裝物件只是快取。
        private Dictionary<int, AirTrackState> externalStates = new Dictionary<int, AirTrackState>();
        private List<int> externalStateKeys;
        private List<AirTrackState> externalStateValues;
        private readonly Dictionary<int, ExternalAirTrack> externalTracks = new Dictionary<int, ExternalAirTrack>();

        /// <summary>所有航跡：飛彈、空投幽靈、實作 <see cref="IAirTrack"/> 的世界物件與 <see cref="AirTrackProvider"/> 提供的物件。</summary>
        public IEnumerable<IAirDefenseTrack> Tracks
        {
            get
            {
                List<WorldObject> objects = Find.WorldObjects.AllWorldObjects;
                for (int i = 0; i < objects.Count; i++)
                {
                    WorldObject o = objects[i];
                    if (o.Destroyed) { continue; }
                    if (o is IAirDefenseTrack own) { yield return own; }
                    else if (o is IAirTrack air) { yield return ExternalFor(air); }
                }
                List<IAirTrack> provided = AirDefenseExtensions.CollectProvidedTracks();
                for (int i = 0; i < provided.Count; i++)
                {
                    WorldObject self = provided[i]?.Self;
                    if (self == null || self.Destroyed || self is IAirTrack) { continue; }
                    yield return ExternalFor(provided[i]);
                }
            }
        }

        /// <summary>只有飛彈：導引干擾與玩家飛彈警示只對它們有意義。</summary>
        public IEnumerable<WorldObject_Missile> MissileTracks => Find.WorldObjects.AllWorldObjects.OfType<WorldObject_Missile>()
            .Where(t => !t.Destroyed);
        public IEnumerable<IAirDefenseTrack> PlayerIncoming => Tracks.Where(t => t.State.detectedByFaction == Faction.OfPlayer
            && TargetFaction(t) == Faction.OfPlayer).OrderBy(t => t.TicksToArrival);
        public static Faction TargetFaction(IAirDefenseTrack track) => track.TargetParent?.Faction
            ?? Find.WorldObjects.MapParentAt(track.Destination)?.Faction;

        private ExternalAirTrack ExternalFor(IAirTrack track)
        {
            int id = track.Self.ID;
            if (externalTracks.TryGetValue(id, out ExternalAirTrack known))
            {
                known.Inner = track;
                return known;
            }
            if (!externalStates.TryGetValue(id, out AirTrackState state))
            {
                state = new AirTrackState();
                externalStates[id] = state;
            }
            var created = new ExternalAirTrack(track, state);
            externalTracks[id] = created;
            return created;
        }

        /// <summary>世界物件對應的航跡；不是航跡時回傳 null。</summary>
        public IAirDefenseTrack TrackOf(WorldObject worldObject)
        {
            if (worldObject == null) { return null; }
            if (worldObject is IAirDefenseTrack own) { return own; }
            if (worldObject is IAirTrack air) { return worldObject.Destroyed ? externalTracks.GetValueOrDefault(worldObject.ID) : ExternalFor(air); }
            if (externalTracks.TryGetValue(worldObject.ID, out ExternalAirTrack known)) { return known; }
            // 剛讀檔還沒掃描過：來源提供的航跡要問過來源才知道。
            return Tracks.FirstOrDefault(t => t.Self == worldObject);
        }

        /// <summary>
        /// 外部航跡（<see cref="IAirTrack"/>）抵達目的地時呼叫，在移除物件或處理抵達效果<b>之前</b>：
        /// 發出 <see cref="AirCombatEventKind.TrackArrived"/>，並把受襲通報交給 NPC 防空警戒（與飛彈抵達相同）。
        /// 飛彈與空投幽靈自己會處理，不必呼叫。回傳目標據點（可能為 null）。
        /// </summary>
        public static MapParent ReportArrival(IAirTrack track)
        {
            if (track?.Self == null) { return null; }
            WorldComponent_AirDefenseNetwork network = Current;
            IAirDefenseTrack wrapped = track as IAirDefenseTrack ?? network?.TrackOf(track.Self);
            if (wrapped == null) { return null; }
            MapParent site = WorldComponent_FactionAirDefenseAlert.TargetSite(wrapped);
            WorldComponent_FactionAirDefenseAlert.Current?.ReportHit(wrapped, site);
            AirDefenseHooks.WorldEvent(AirCombatEventKind.TrackArrived, wrapped, site?.Faction, site);
            return site;
        }

        /// <summary>丟掉已不存在或來源不再提供的外部航跡。</summary>
        private void PruneExternal(List<IAirDefenseTrack> live)
        {
            if (externalStates.Count == 0 && externalTracks.Count == 0) { return; }
            HashSet<int> ids = new HashSet<int>();
            foreach (IAirDefenseTrack track in live)
            {
                if (track is ExternalAirTrack) { ids.Add(track.Self.ID); }
            }
            foreach (int id in externalStates.Keys.Where(k => !ids.Contains(k)).ToList()) { externalStates.Remove(id); }
            foreach (int id in externalTracks.Keys.Where(k => !ids.Contains(k)).ToList()) { externalTracks.Remove(id); }
        }
        /// <summary>
        /// Engagement order: soonest arrival first (stable, so ties keep creation order). Free channels go to
        /// the most urgent tracks, which makes decoy waves predictable (docs/BVR_波次打擊規劃.md §11).
        /// </summary>
        public static List<T> InEngagementOrder<T>(IEnumerable<T> tracks) where T : IAirDefenseTrack
            => tracks.OrderBy(t => t.TicksToArrival).ToList();
        /// <summary>
        /// With threat assessment (P4) a node engages the largest signature first; <paramref name="priority"/> returns
        /// that signature for tracks heading to such a node and 0 otherwise. Ties fall back to soonest arrival.
        /// </summary>
        public static List<T> InEngagementOrder<T>(IEnumerable<T> tracks, System.Func<T, float> priority) where T : IAirDefenseTrack
            => tracks.OrderByDescending(priority).ThenBy(t => t.TicksToArrival).ToList();
        /// <summary>
        /// 接戰節點：目的地節點優先（點防禦）；否則由最近的「看得到或航跡飛越其覆蓋範圍」的節點接手
        /// （P4 區域防禦）。NPC 節點依警戒階段政策篩選，且本陣營必須在 <paramref name="intelSince"/> 之後
        /// 實際看到這條航跡（本地或同陣營資料鏈），避免先選最近節點後才發現沒有情報而放棄其他可用節點。
        /// </summary>
        private AirDefenseNode NodeFor(IAirDefenseTrack track, int intelSince, bool checkAltitude = true)
        {
            AirDefenseNode best = null;
            float altitude = track.AltitudeAt(0);
            float bestDistance = float.MaxValue;
            foreach (AirDefenseNode n in scanNodes)
            {
                if (n.parent == null || n.faction == null || !n.PolicyAllows(track)) { continue; }
                if (intelSince != int.MinValue && !track.State.FactionSees(n.faction, intelSince)) { continue; }
                if (!n.CanEngage(track)) { continue; }
                if (!n.CoversDomain(CombatDomainUtility.Of(track))) { continue; }
                if (checkAltitude && !n.CanReachAltitude(altitude)) { continue; }
                float distance = MissileWorldUtility.SurfaceTilesBetween(n.Center, track.PositionAt(0));
                bool better = best == null
                    || (n.DestinationIs(track) && !best.DestinationIs(track))
                    || (n.DestinationIs(track) == best.DestinationIs(track)
                        && (distance < bestDistance - 0.01f
                            || (Mathf.Abs(distance - bestDistance) <= 0.01f && n.faction.IsPlayer && !best.faction.IsPlayer)));
                if (better) { best = n; bestDistance = distance; }
            }
            return best;
        }

        /// <summary>
        /// 數據鏈（P4）：任何節點看到航跡都算「該陣營已偵測」，不要求目的地節點自己看到。
        /// 每個陣營各自記錄情報（<see cref="AirDefenseObservation"/>），NPC 節點同時把觀測交給警戒系統入帳。
        /// 回傳玩家介面用的代表探測者：玩家優先，其餘取最近的探測者。
        /// </summary>
        private AirDefenseNode Observe(IAirDefenseTrack track, int now)
        {
            AirDefenseNode best = null;
            float bestDistance = float.MaxValue;
            WorldComponent_FactionAirDefenseAlert alert = WorldComponent_FactionAirDefenseAlert.Current;
            MapParent targetSite = null;
            bool targetResolved = false;
            foreach (AirDefenseNode n in scanNodes)
            {
                if (n.parent == null || n.faction == null || !n.MayObserve(track)) { continue; }
                if (!n.CanSee(track)) { continue; }
                if (!targetResolved)
                {
                    targetSite = WorldComponent_FactionAirDefenseAlert.TargetSite(track);
                    targetResolved = true;
                }
                AirDefenseObservation observation = track.State.ObservationOf(n.faction, create: true);
                bool firstSight = observation.firstSeenTick < 0;
                if (firstSight) { observation.firstSeenTick = now; }
                observation.lastSeenTick = now;
                observation.lastPosition = track.PositionAt(0);
                observation.knownDestination = track.Destination;
                observation.knownTargetFaction = targetSite?.Faction;
                if (n.UsesAlertRules) { alert?.ReportObservation(n, track, observation, now); }
                if (firstSight) { AirDefenseHooks.WorldEvent(AirCombatEventKind.TrackDetected, track, n.faction, n.parent, n.local?.map); }
                float distance = MissileWorldUtility.SurfaceTilesBetween(n.Center, track.PositionAt(0));
                // 代表探測者：玩家優先，其次是被瞄準的陣營（NPC 也會觀測中立過境，避免由旁觀者代表），再取最近。
                int rank = n.faction.IsPlayer ? 2 : n.faction == targetSite?.Faction ? 1 : 0;
                int bestRank = best == null ? -1 : best.faction.IsPlayer ? 2 : best.faction == targetSite?.Faction ? 1 : 0;
                bool better = rank > bestRank || (rank == bestRank && distance < bestDistance);
                if (better) { best = n; bestDistance = distance; }
            }
            return best;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref externalStates, "externalTrackStates", LookMode.Value, LookMode.Deep, ref externalStateKeys, ref externalStateValues);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                rebuild = true;
                externalStates ??= new Dictionary<int, AirTrackState>();
                externalTracks.Clear();
            }
        }
        public void AddFlash(Vector3 point) => drawer.AddFlash(point);
        // ---- 佔用中的火力通道（M10 效能註冊表） ----
        // 抽象節點每次判斷剩餘通道都要數一次；以前每次都掃描全部航跡與世界物件。
        // 現在每 tick 最多算一次，鎖定、解鎖、攔截彈發射或移除時重算（NotifyEngagementChanged）。
        // 航跡在同一 tick 內消失時，計數要到下一 tick 才減少：只會少開火，不會超用通道。
        private static int engagedVersion;
        private int engagedCacheTick = -1;
        private int engagedCacheVersion = -1;
        private readonly Dictionary<long, int> engagedCache = new Dictionary<long, int>();

        /// <summary>鎖定或在飛攔截彈有變動；下次 <see cref="CountEngaged"/> 會重算。</summary>
        public static void NotifyEngagementChanged() => engagedVersion++;

        private static long EngagedKey(MapParent parent, Faction faction) => ((long)parent.ID << 32) | (uint)faction.loadID;

        public int CountEngaged(MapParent parent, Faction faction)
        {
            if (parent == null || faction == null) { return 0; }
            int now = Find.TickManager.TicksGame;
            if (now != engagedCacheTick || engagedVersion != engagedCacheVersion) { RebuildEngaged(now); }
            return engagedCache.TryGetValue(EngagedKey(parent, faction), out int count) ? count : 0;
        }

        private void RebuildEngaged(int now)
        {
            engagedCache.Clear();
            engagedCacheTick = now;
            engagedCacheVersion = engagedVersion;
            foreach (IAirDefenseTrack track in Tracks)
            {
                AirTrackState state = track.State;
                if (state.lockUntil < 0 || state.lockNode == null || state.lockFaction == null) { continue; }
                long key = EngagedKey(state.lockNode, state.lockFaction);
                engagedCache[key] = engagedCache.GetValueOrDefault(key) + 1;
            }
            List<WorldObject> objects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (!(objects[i] is WorldObject_Interceptor interceptor) || interceptor.Destroyed || !interceptor.usesChannel) { continue; }
                if (interceptor.launchNode == null || interceptor.Faction == null) { continue; }
                long key = EngagedKey(interceptor.launchNode, interceptor.Faction);
                engagedCache[key] = engagedCache.GetValueOrDefault(key) + 1;
            }
        }
        /// <summary>完整的節點清單（介面、封鎖圈、開發工具）。</summary>
        private void RebuildNodes()
        {
            BuildNodes(nodes, activeOnly: false);
            nodesDirty = false;
            lastRebuildTick = Find.TickManager.TicksGame;
        }

        /// <summary>掃描用的節點：地圖節點，加上目前在活躍期內的抽象節點。</summary>
        private void RebuildScanNodes()
        {
            BuildNodes(scanNodes, activeOnly: true);
            autonomousNodes.Clear();
            foreach (AirDefenseNode node in scanNodes)
            {
                if (node.HasAutonomousEffector) { autonomousNodes.Add(node); }
            }
        }

        private void BuildNodes(List<AirDefenseNode> nodes, bool activeOnly)
        {
            int now = Find.TickManager.TicksGame;
            nodes.Clear();
            foreach (Map map in Find.Maps)
            {
                MapComponent_AirDefense local = map.GetComponent<MapComponent_AirDefense>();
                if (local == null) { continue; }
                var factions = AirSensors.On(map).Select(s => s.Source?.Faction)
                    .Concat(local.fireControlRadars.Select(r => r.parent.Faction)).Where(f => f != null).Distinct();
                foreach (Faction faction in factions)
                    nodes.Add(new AirDefenseNode { parent = map.Parent, faction = faction, local = local });
            }
            // Abstract NPC air-defence nodes are a world-map radar-network feature. Keep physical map
            // radars above active for local combat, but do not synthesize or register remote NPC nodes
            // until the player has researched the radar detection network.
            // 例外：標記 ignoreNetworkResearch 的抽象防空（別的 mod 的核心敵人）不等研究就建立；合成防空仍要研究。
            bool npcWorldNetworkEnabled = WorldComponent_FactionAirDefenseAlert.Enabled;
            foreach (MapParent parent in Find.WorldObjects.AllWorldObjects.OfType<MapParent>())
            {
                if (parent == null || parent.Destroyed) { continue; }
                WorldObjectComp_AirDefense comp = parent.GetComponent<WorldObjectComp_AirDefense>();
                if (!npcWorldNetworkEnabled && comp?.Props.ignoreNetworkResearch != true) { continue; }
                if (activeOnly && !(nodeActiveUntil.TryGetValue(parent.ID, out int until) && until >= now)) { continue; }
                if (comp?.Available == true)
                {
                    nodes.Add(new AirDefenseNode { parent = parent, faction = parent.Faction, defense = new WorldAirDefenseAdapter(comp) });
                    continue;
                }
                // 一般據點自動防空（P4）：敵方、無地圖、沒有專屬防空元件的據點，依科技等級合成一套。
                IAirDefense synthetic = WorldComponent_SettlementDefense.Current?.DefenseFor(parent);
                if (synthetic != null && synthetic.Available)
                {
                    nodes.Add(new AirDefenseNode { parent = parent, faction = parent.Faction, defense = synthetic });
                }
            }
            ApplyAlertStages(nodes);
            foreach (AirDefenseNode node in nodes)
            {
                node.jamEff = EWEnvironment.NodeJam(node);
            }
        }

        /// <summary>
        /// 完整檢查（長週期、讀檔後、或來源不明的變動）：世界上有航跡就讓掃描保持到下一次長週期檢查，並替每條航跡啟動航線上的節點。
        /// 沒有航跡時什麼都不做，活躍期自然過期。
        /// </summary>
        private void CheckPresence(int now)
        {
            presenceCheckDue = false;
            pendingTracks.Clear();
            List<IAirDefenseTrack> live = Tracks.ToList();
            PruneExternal(live);
            if (live.Count == 0) { return; }
            EnsureActivationNodes(now);
            foreach (IAirDefenseTrack track in live) { ActivateNodesFor(track, now); }
            if (nodeActiveUntil.Count > 64)
            {
                foreach (int id in nodeActiveUntil.Where(p => p.Value < now).Select(p => p.Key).ToList()) { nodeActiveUntil.Remove(id); }
            }
        }

        /// <summary>只處理剛加入或剛改靶的航跡（<see cref="NotifyTrackChanged(WorldObject)"/>）。</summary>
        private void ActivatePending(int now)
        {
            bool prepared = false;
            for (int i = 0; i < pendingTracks.Count; i++)
            {
                WorldObject o = pendingTracks[i];
                if (o == null || o.Destroyed) { continue; }
                IAirDefenseTrack track = TrackOf(o);
                if (track == null) { continue; }
                if (!prepared) { EnsureActivationNodes(now); prepared = true; }
                ActivateNodesFor(track, now);
            }
            pendingTracks.Clear();
        }

        /// <summary>
        /// 啟動判定只需要「有哪些抽象節點、各在哪裡、設備探測半徑多大」，不必每次重建：清單被作廢（階段改變、新據點）或
        /// 已經過了一個長週期才重建。
        /// </summary>
        private void EnsureActivationNodes(int now)
        {
            if (nodesDirty || lastRebuildTick < 0 || now - lastRebuildTick >= PresenceCheckIntervalTicks)
            {
                RebuildNodes();
                evaluatedRoutes.Clear(); // 節點可能變了：航線要對新的清單重新判定
            }
        }

        /// <summary>下一次長週期檢查的 tick；掃描與節點的活躍期都對齊到它，所以同一個週期內啟動過的節點不必重算。</summary>
        private static int NextPresenceCheck(int now) => (now / PresenceCheckIntervalTicks + 1) * PresenceCheckIntervalTicks;

        /// <summary>
        /// 讓掃描保持到下一次長週期檢查，並把這條航線在那之前會進入其探測範圍（或以它為目的地）的抽象節點標成活躍。
        /// </summary>
        private void ActivateNodesFor(IAirDefenseTrack track, int now)
        {
            int nextCheck = NextPresenceCheck(now);
            if (scanActiveUntil < nextCheck) { scanActiveUntil = nextCheck; }
            int until = nextCheck + Mathf.Max(1, AirDefenseTuningDef.Current.scanIntervalTicks);
            // 封鎖違規者只由封鎖據點接戰：直接啟動它們，不看航線與探測半徑。
            BlockadeViolatorTrack violator = BlockadeViolatorTrack.Of(track);
            if (violator != null)
            {
                foreach (BlockadeZone zone in violator.Zones)
                {
                    if (zone.site != null) { nodeActiveUntil[zone.site.ID] = until; }
                }
                return;
            }
            int horizon = Mathf.Clamp(track.TicksToArrival, 0, until - now);
            Vector3 start = track.PositionAt(0);
            float straight = MissileWorldUtility.SurfaceTilesBetween(start, track.PositionAt(horizon));
            AirTrackSignature signature = track.TargetProps.Signature;
            for (int i = evaluatedRoutes.Count - 1; i >= 0; i--)
            {
                EvaluatedRoute known = evaluatedRoutes[i];
                if (known.until != until) { evaluatedRoutes.RemoveAt(i); continue; } // 上一個週期的
                if (known.origin == track.Origin && known.destination == track.Destination && known.signature.SameAs(signature)
                    && straight <= known.reachTiles + 0.25f
                    && MissileWorldUtility.SurfaceTilesBetween(known.start, start) <= 0.5f)
                {
                    return;
                }
            }
            evaluatedRoutes.Add(new EvaluatedRoute { origin = track.Origin, destination = track.Destination, signature = signature, start = start, reachTiles = straight, until = until });
            bool sampled = false;
            float pathLength = 0f, slack = 0f;
            foreach (AirDefenseNode node in nodes)
            {
                if (node.defense == null || node.parent == null || node.parent.Destroyed) { continue; }
                int id = node.parent.ID;
                // 齊射：前一枚已經啟動過的節點直接跳過。
                if (nodeActiveUntil.TryGetValue(id, out int known) && known >= until) { continue; }
                bool near = node.DestinationIs(track);
                if (!near)
                {
                    if (!sampled)
                    {
                        // 沿預測航線每格左右取一點（第一個需要的節點才算，全部節點都已活躍時整條省掉）。
                        sampled = true;
                        int steps = Mathf.Clamp(Mathf.CeilToInt(straight), 1, 96);
                        pathScratch.Clear();
                        pathScratch.Add(start);
                        float longest = 0f;
                        for (int i = 1; i <= steps; i++)
                        {
                            Vector3 point = track.PositionAt(Mathf.RoundToInt((float)horizon * i / steps));
                            float segment = MissileWorldUtility.SurfaceTilesBetween(pathScratch[i - 1], point);
                            pathLength += segment;
                            longest = Mathf.Max(longest, segment);
                            pathScratch.Add(point);
                        }
                        // 取樣間距的一半也算進去：航線從兩個取樣點之間擦過探測圈時不會漏掉。
                        slack = ActivationPaddingTiles + longest * 0.5f;
                    }
                    float reach = node.EquipmentDetectionRange(track.TargetProps, track) + slack;
                    Vector3 center = node.Center;
                    float fromStart = MissileWorldUtility.SurfaceTilesBetween(center, start);
                    // 離起點比「整條航線長度＋探測半徑」還遠的節點不可能碰到航線：絕大多數節點只算這一次距離。
                    if (fromStart <= reach) { near = true; }
                    else if (fromStart <= pathLength + reach)
                    {
                        for (int i = 1; i < pathScratch.Count && !near; i++)
                        {
                            near = MissileWorldUtility.SurfaceTilesBetween(center, pathScratch[i]) <= reach;
                        }
                    }
                }
                if (near) { nodeActiveUntil[id] = until; }
            }
        }

        /// <summary>
        /// NPC 節點套用警戒階段（docs/NPC防空警戒五階段_執行計畫.md §3）。只套用在節點陣營等於據點陣營的情況；
        /// 玩家節點、或別的陣營放在這張地圖上的裝置維持舊規則。
        /// </summary>
        private static void ApplyAlertStages(List<AirDefenseNode> nodes)
        {
            WorldComponent_FactionAirDefenseAlert alert = WorldComponent_FactionAirDefenseAlert.Current;
            bool enabled = alert != null && WorldComponent_FactionAirDefenseAlert.Enabled;
            foreach (AirDefenseNode node in nodes)
            {
                node.alertStage = 0;
                node.rangeFactor = 1f;
                if (!enabled || node.faction == null || node.faction.IsPlayer || node.parent?.Faction != node.faction) { continue; }
                int stage = alert.StageOf(node.parent);
                if (stage <= 0) { continue; }
                node.alertStage = stage;
                node.rangeFactor = AirDefenseAlertPolicy.DetectionFactor(stage, alert.RadarWarm(node.parent));
            }
        }

        private int lastRebuildTick = -1;

        /// <summary>
        /// 所有節點（封鎖圈與介面使用）。掃描不再每個週期重建這份清單，所以讀的時候才更新：
        /// 被標成過期（<see cref="MarkNodesDirty"/>）、讀檔後、或已經舊了 <see cref="NodeListMaxAgeTicks"/> 就重建，同一 tick 內最多一次。
        /// </summary>
        public List<AirDefenseNode> Nodes
        {
            get
            {
                int now = Find.TickManager.TicksGame;
                if (lastRebuildTick != now
                    && (nodesDirty || rebuild || lastRebuildTick < 0 || now - lastRebuildTick >= NodeListMaxAgeTicks))
                {
                    RebuildNodes();
                }
                return nodes;
            }
        }

        public AirDefenseNode NodeOf(MapParent parent, Faction faction = null)
            => Nodes.FirstOrDefault(n => n.parent == parent && (faction == null ? n.faction == parent.Faction : n.faction == faction));
        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            int now = Find.TickManager.TicksGame;
            SampleMovement(now);
            if (rebuild)
            {
                pending.Clear();
                pending.AddRange(Find.WorldObjects.AllWorldObjects.OfType<WorldObject_Interceptor>().Where(i => !i.Destroyed));
                pending.Sort((a, b) => a.interceptTick.CompareTo(b.interceptTick));
                rebuild = false;
            }
            // Sorted by due tick; only inspect the head each tick.
            while (pending.Count > 0 && (pending[0].Destroyed || pending[0].interceptTick <= now))
            {
                var interceptor = pending[0];
                pending.RemoveAt(0);
                if (!interceptor.Destroyed) Resolve(interceptor);
            }
            if (presenceCheckDue || now % PresenceCheckIntervalTicks == 0) { CheckPresence(now); }
            if (now % Mathf.Max(1, AirDefenseTuningDef.Current.scanIntervalTicks) != 0) return;
            if (pendingTracks.Count > 0) { ActivatePending(now); }
            if (now > scanActiveUntil) return;
            // 先做干擾機進圈判定：被判定消失的彈不會先被鎖定、白射一枚攔截彈（docs/電子戰建築_實作計畫.md §3.2）。
            JammerUtility.ProcessTracks(MissileTracks);
            RebuildScanNodes();
            List<IAirDefenseTrack> live = Tracks.ToList();
            PruneExternal(live);
            // 排序只看哪個節點會接手（不看本次掃描的情報），與接戰時的情報檢查分開。
            foreach (IAirDefenseTrack track in InEngagementOrder(live,
                t => NodeFor(t, int.MinValue)?.ThreatAssessment == true ? t.TargetProps.distanceLevel : 0f))
            {
                // 事件處理者可能已經移除後面的航跡。
                if (!track.Self.Destroyed) { Scan(track, now); }
            }
        }
        private void Scan(IAirDefenseTrack track, int now)
        {
            AirTrackState state = track.State;
            // 箔條成功斷鎖後的延遲期：這段時間內不重新鎖定（等於白吃一次反應時間）。
            if (now < state.lockDelayUntil) { return; }
            if (state.lastKnownDestination != track.Destination)
            {
                state.ClearLock();
                state.lastKnownDestination = track.Destination;
                NotifyTrackChanged(track.Self); // 新航線可能經過還沒活躍的節點
            }
            // 數據鏈：任何會觀測它的節點看到就算該陣營偵測到，不要求「目的地節點」自己看到。
            AirDefenseNode detector = Observe(track, now);
            if (detector == null)
            {
                state.ClearLock();
                state.detectedByFaction = null;
                return;
            }
            state.detectedByFaction = detector.faction;
            if (state.firstDetectedTick < 0) { state.firstDetectedTick = now; }
            if (detector.faction.IsPlayer && !state.playerNotified)
            {
                state.playerNotified = true;
                // 一小時內的後續偵測改用訊息，不再每發一封信件並暫停遊戲。
                MissileAlertThrottle.Notify("Detected", "FFF.BVR.World.DetectedLetter".Translate(),
                    "FFF.BVR.World.DetectedLetterText".Translate(track.Self.Label, detector.parent.Label,
                        track.TicksToArrival.ToStringTicksToPeriod()), LetterDefOf.ThreatBig,
                    new GlobalTargetInfo(track.Self));
            }
            // 已被鎖定時，箔條／干擾絲有機會打斷這次鎖定。
            TryExpendChaff(track, now);
            if (FireAutonomous(track, now)) return;
            if (state.interceptor != null && !state.interceptor.Destroyed) return;
            state.interceptor = null;

            // 區域防禦：目的地節點優先，否則由最近的可用節點接手。
            // 資料鏈：本節點自己看不到時，只要同陣營本次掃描看到仍可接戰（NodeFor 已檢查情報）。
            // 每次掃描都重查政策：鎖定期間降級、聯盟改變或改靶都會在發射前放掉鎖定。
            AirDefenseNode node = NodeFor(track, now);
            state.altitudeDenied = node == null && NodeFor(track, now, checkAltitude: false) != null;
            if (node == null)
            {
                state.ClearLock();
                return;
            }
            // 已被這個陣營識破的誘餌：不再鎖定或發射（docs/MIRV分導彈頭與誘餌載荷_實作計畫.md §7.3）。
            if (state.DecoyIdentifiedBy(node.faction))
            {
                state.ClearLock();
                return;
            }
            if (state.lockUntil >= 0 && (state.lockNode != node.parent || state.lockFaction != node.faction)) state.ClearLock();
            int eta = track.TicksToArrival;
            var target = new AirDefenseTarget { stealthLevel = track.TargetProps.stealthLevel, speed = track.TargetProps.speed, altitude = track.AltitudeAt(0),
                domain = CombatDomainUtility.Of(track) };
            CompFireControlRadar radar = node.BestRadar(target, eta);
            CompProperties_FireControlRadar settings = radar?.Props ?? node.DefenseFireControl;
            if (settings == null || eta > settings.maxRangeTicks || !settings.InAltitudeEnvelope(target.altitude) || !settings.Covers(target.domain))
            {
                state.ClearLock();
                return;
            }
            // No interceptor that can fire now or after reloading: do not lock, and drop any lock (frees the channel).
            bool armed = node.HasInterceptorAmmoFor(target.domain);
            if (state.lockUntil < 0)
            {
                if (!armed || node.FreeChannels <= 0) return;
                // 否決者（AirCombatVeto）可以擋下這次鎖定：不鎖定、不佔通道，下次掃描再問。
                if (!AirDefenseHooks.WorldAllows(AirCombatEventKind.LockStarted, track, node.faction, node.parent, node.local?.map, effector: radar?.parent)) return;
                int lockTicks = radar != null ? radar.LockOnTicksFor(target) : CompFireControlRadar.LockOnTicksFor(settings, target);
                float lockCost = AirDefenseHooks.WorldStat(AirDefenseStat.LockTicks, lockTicks * EWEnvironment.LockFactor(node.jamEff),
                    node, track, null, sensor: radar?.parent, eta: eta);
                state.lockUntil = now + Mathf.Max(1, Mathf.RoundToInt(lockCost));
                state.lockNode = node.parent;
                state.lockFaction = node.faction;
                state.lockNodeTile = node.Tile;
                NotifyEngagementChanged();
                if (node.UsesAlertRules) { WorldComponent_FactionAirDefenseAlert.Current?.NotifyEngagingPlayer(node, track); }
                AirDefenseHooks.WorldEvent(AirCombatEventKind.LockStarted, track, node.faction, node.parent, node.local?.map, effector: radar?.parent);
                return;
            }
            if (!armed)
            {
                state.ClearLock();
                return;
            }
            if (state.lockUntil > now) return;
            // 鎖定完成：識別誘餌（每陣營一次）。識破就放掉鎖定，代價是已花掉的鎖定時間。
            if (TryDiscriminate(track, node, settings, radar, eta, now))
            {
                state.ClearLock();
                return;
            }
            AirEngagement engagement = node.EngagementFor(track, track.Self, track.PositionAt(0), eta, target.altitude);
            IAirEffector effector = node.ReadyEffector(now, engagement);
            if (effector == null) return;
            AirEffectorShot shot = effector.PrepareShot(engagement);
            if (!SolveShot(node, track, shot, out int ticks, out Vector3 point))
            {
                state.ClearLock();
                return;
            }
            // 命中率看會合點的高度：飛行剖面可能在攔截彈到達前離開可攔截的高度。
            target.altitude = track.AltitudeAt(ticks);
            if (!settings.InAltitudeEnvelope(target.altitude))
            {
                state.ClearLock();
                return;
            }
            float baseChance = radar != null ? radar.ComputeHitChance(target, eta) : CompFireControlRadar.ComputeHitChance(settings, target, eta);
            float chance = ShotChance(baseChance, shot, node, track, radar?.parent, eta);
            if (!AirDefenseHooks.WorldAllows(AirCombatEventKind.InterceptorLaunched, track, node.faction, node.parent, node.local?.map,
                shot.Loadout, effector.Source, chance))
            {
                state.ClearLock();
                return;
            }
            if (!effector.TryFire(engagement, now, point)) return;
            WorldObject_Interceptor interceptor = Launch(node, track, effector, shot, chance, ticks, point, now, usesChannel: true);
            state.ClearLock();
            state.interceptor = interceptor;
            state.interceptAttempts++;
        }

        /// <summary>
        /// 誘餌識別骰：只對誘餌、每陣營一次。回傳 true 表示這次識破。
        /// </summary>
        private static bool TryDiscriminate(IAirDefenseTrack track, AirDefenseNode node, CompProperties_FireControlRadar settings,
            CompFireControlRadar radar, int eta, int now)
        {
            AirTrackState state = track.State;
            if (!DecoyDiscrimination.IsDecoy(track, out int fidelity) || state.DecoyRolledBy(node.faction)) { return false; }
            bool firstForAnyone = state.decoyIdentifiedFactions.NullOrEmpty();
            float chance = AirDefenseHooks.WorldStat(AirDefenseStat.Discrimination, DecoyDiscrimination.BaseChance(settings, fidelity),
                node, track, null, sensor: radar?.parent, eta: eta);
            bool identified = Rand.Chance(chance);
            state.RecordDecoyRoll(node.faction, identified);
            if (Prefs.DevMode)
            {
                Log.Message($"[Fortified MIRV] {node.faction?.Name} 對誘餌 #{track.Self.ID} 識別骰 {chance:P0} → {(identified ? "識破" : "未識破")}");
            }
            if (!identified) { return false; }
            if (firstForAnyone && track.Self is WorldObject_Missile missile)
            {
                WorldComponent_MirvGroups.RecordDecoyIdentified(missile.mirvGroupId);
            }
            AirDefenseHooks.WorldEvent(AirCombatEventKind.DecoyIdentified, track, node.faction, node.parent, node.local?.map, effector: radar?.parent);
            DecoyDiscrimination.NotifyIdentified(node.faction, track.Self.LabelCap, new GlobalTargetInfo(track.Self));
            return true;
        }

        /// <summary>點防禦式武器：不鎖定、不佔通道，也不受在飛的攔截彈影響。回傳 true 表示航跡已被移除。</summary>
        private bool FireAutonomous(IAirDefenseTrack track, int now)
        {
            if (autonomousNodes.Count == 0) { return false; }
            float altitude = track.AltitudeAt(0);
            int eta = track.TicksToArrival;
            Vector3 position = track.PositionAt(0);
            foreach (AirDefenseNode node in autonomousNodes)
            {
                if (node.parent == null || node.faction == null || !track.State.FactionSees(node.faction, now) || !node.PolicyAllows(track)) { continue; }
                if (track.State.DecoyIdentifiedBy(node.faction)) { continue; } // 點防禦不識別，但不打陣營已識破的誘餌
                AirEngagement engagement = node.EngagementFor(track, track.Self, position, eta, altitude);
                CombatDomainDef domain = engagement.Domain;
                foreach (IAirEffector effector in node.Effectors.Where(e => !e.UsesFireControl).ToList())
                {
                    if (!CombatDomainUtility.Engages(effector, domain) || !effector.CanFire(now) || !effector.CanEngage(engagement)) { continue; }
                    AirEffectorShot shot = effector.PrepareShot(engagement);
                    if (!SolveShot(node, track, shot, out int ticks, out Vector3 point)) { continue; }
                    float chance = ShotChance(shot.BaseHitChance, shot, node, track, effector.Source, eta);
                    if (!AirDefenseHooks.WorldAllows(AirCombatEventKind.InterceptorLaunched, track, node.faction, node.parent, node.local?.map,
                        shot.Loadout, effector.Source, chance)) { continue; }
                    if (!effector.TryFire(engagement, now, point)) { continue; }
                    Launch(node, track, effector, shot, chance, ticks, point, now, usesChannel: false);
                    track.State.interceptAttempts++;
                    if (track.Self.Destroyed) { return true; }
                }
            }
            return false;
        }

        private static bool SolveShot(AirDefenseNode node, IAirDefenseTrack track, AirEffectorShot shot, out int ticks, out Vector3 point)
        {
            if (shot.AngularSpeed <= 0f)
            {
                ticks = 0;
                point = track.PositionAt(0);
                return true;
            }
            return node.DestinationIs(track)
                ? InterceptSolver.TrySolve(track, node.Center, shot.AngularSpeed, out ticks, out point)
                : InterceptSolver.TrySolveGeneral(track, node.Center, shot.AngularSpeed, out ticks, out point);
        }

        private static float ShotChance(float baseChance, AirEffectorShot shot, AirDefenseNode node, IAirDefenseTrack track, Thing sensor, int eta)
            => AirDefenseHooks.WorldStat(AirDefenseStat.HitChance, Mathf.Clamp01(baseChance * shot.HitFactor + shot.HitBonus),
                node, track, null, shot.Loadout, sensor, eta);

        /// <summary>
        /// 武器已開火：瞬間命中當場結算並回傳 null；否則產生攔截物，到時由 <see cref="Resolve"/> 結算。
        /// </summary>
        private WorldObject_Interceptor Launch(AirDefenseNode node, IAirDefenseTrack track, IAirEffector effector, AirEffectorShot shot,
            float chance, int ticks, Vector3 point, int now, bool usesChannel)
        {
            void Announce() => AirDefenseHooks.WorldEvent(AirCombatEventKind.InterceptorLaunched, track, node.faction, node.parent, node.local?.map,
                shot.Loadout, effector.Source, chance);
            BlockadeViolatorTrack.Of(track)?.NotifyEngaged(node);
            if (shot.AngularSpeed <= 0f)
            {
                Announce();
                RollShot(track, node.faction, node.parent, chance, shot.ExtraRolls, point, AirTrackDisplay.CurrentLift(track));
                return null;
            }
            WorldObjectDef def = shot.ShotDef ?? DefDatabase<WorldObjectDef>.GetNamedSilentFail("FFF_Interceptor");
            var interceptor = def != null ? WorldObjectMaker.MakeWorldObject(def) as WorldObject_Interceptor : null;
            if (interceptor == null)
            {
                // 彈藥已經扣了：沒有可用的攔截物定義時當場結算，這一發不會憑空消失。
                Log.ErrorOnce($"[Fortified BVR] {def?.defName ?? "FFF_Interceptor"} is not a {nameof(WorldObject_Interceptor)} world object.", 0x424c53);
                Announce();
                RollShot(track, node.faction, node.parent, chance, shot.ExtraRolls, point, AirTrackDisplay.CurrentLift(track));
                return null;
            }
            interceptor.Tile = node.Tile;
            interceptor.SetFaction(node.faction);
            interceptor.launchNode = node.parent;
            interceptor.launchTick = now;
            interceptor.interceptTick = now + ticks;
            interceptor.start = node.Center;
            interceptor.end = point;
            interceptor.target = track.Self;
            interceptor.targetDestination = track.Destination;
            interceptor.targetRevision = track.RouteRevision;
            interceptor.hitChance = chance;
            interceptor.extraRolls = shot.ExtraRolls;
            interceptor.usesChannel = usesChannel;
            MissileConfig config = shot.Loadout as MissileConfig;
            interceptor.warhead = config?.PartFor(MissilePartCategoryDefOf.Warhead);
            interceptor.payloadCapacity = config?.PayloadCapacity ?? 0f;
            Find.WorldObjects.Add(interceptor);
            NotifyEngagementChanged();
            pending.Add(interceptor);
            pending.Sort((a, b) => a.interceptTick.CompareTo(b.interceptTick));
            Announce();
            return interceptor;
        }

        /// <summary>
        /// P4 空投虛擬航跡：把地圖波次中的空投也表示成一條世界層航跡，讓世界雷達與攔截彈能提前接戰。
        /// 由 Tuning 的 <see cref="AirDefenseTuningDef.podsUseWorldTracks"/> 開啟；導彈本來就有自己的航跡，不重複建立。
        /// </summary>
        public void TrySpawnDropPodTrack(Map map, AirDefenseWave wave, AirDefenseTarget target, Thing faller)
        {
            if (!AirDefenseTuningDef.Current.podsUseWorldTracks) { return; }
            if (map == null || wave == null || target == null || faller == null) { return; }
            if (faller is IIncomingMissile || !(faller is Skyfaller)) { return; }
            Faction defender = wave.defenderFaction;
            if (defender == null) { return; }

            if (Find.WorldObjects.AllWorldObjects.OfType<WorldObject_DropPodTrack>()
                .Any(t => !t.Destroyed && t.targetMap == map && t.waveTargetId == target.id)) { return; }

            AirDefenseNode node = Nodes
                .Where(n => n.faction == defender && n.DetectionRange(AirTargetExtension.Default) > 0f)
                .OrderByDescending(n => n.faction.IsPlayer)
                .FirstOrDefault();
            if (node == null) { return; }

            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("FFF_DropPodTrack");
            if (def == null) { return; }

            int now = Find.TickManager.TicksGame;
            Vector3 end = MissileWorldUtility.TileCenter(map.Tile);
            Vector3 center = node.Center;
            float angle = GenMath.SphericalDistance(center.normalized, end.normalized);
            float range = node.DetectionRange(AirTargetExtension.Default);
            if (angle <= 0.000001f || range <= 0f) { return; }

            float tilesPerRadian = Mathf.Max(1f, Find.WorldGrid.ApproxDistanceInTiles(1f));
            float radiusAngle = range / tilesPerRadian;
            float ratio = Mathf.Clamp(radiusAngle / angle, 0.05f, 0.9f);
            Vector3 start = Vector3.Slerp(end, center, ratio);

            WorldObject_DropPodTrack ghost = (WorldObject_DropPodTrack)WorldObjectMaker.MakeWorldObject(def);
            ghost.Tile = map.Tile;
            ghost.SetFaction(node.faction);
            ghost.spawnTick = now;
            ghost.arrivalTick = Mathf.Max(now + 60, wave.tickToImpact);
            ghost.startPos = start;
            ghost.endPos = end;
            ghost.targetTile = map.Tile;
            ghost.targetMap = map;
            ghost.waveTargetId = target.id;
            ghost.incomingDef = faller.def;
            Find.WorldObjects.Add(ghost);

            // 世界層接手後，地圖內只保留末端 CIWS：中過程改在世界層接戰。
            target.terminalOnly = true;
        }

        /// <summary>擲命中骰、處理空爆波及，發出事件並顯示閃光。呼叫前目標必須仍有效。</summary>
        private void RollShot(IAirDefenseTrack target, Faction by, MapParent site, float chance, int rolls, Vector3 point, float lift)
        {
            WorldObject targetObject = target.Self;
            PlanetTile destination = target.Destination;
            bool hit = Rand.Chance(chance);
            if (hit) target.Notify_Intercepted(by);
            else if (by == Faction.OfPlayer || target.Attacker == Faction.OfPlayer)
                Messages.Message("FFF.BVR.World.Missed".Translate(targetObject.Label), new GlobalTargetInfo(targetObject),
                    MessageTypeDefOf.NeutralEvent, historical: false);
            AirDefenseHooks.WorldEvent(AirCombatEventKind.InterceptResolved, target, by, site, site?.Map,
                hitChance: chance, outcome: hit ? AirInterceptOutcome.Hit : AirInterceptOutcome.Miss);
            if (rolls > 0)
            {
                foreach (var other in Tracks.Where(t => t != target && t.Destination == destination
                    && WouldAirburst(by, t)
                    && MissileWorldUtility.SurfaceTilesBetween(t.PositionAt(0), point) <= AirDefenseTuningDef.Current.airburstRadiusTiles).ToList())
                {
                    if (rolls-- <= 0) break;
                    bool splash = Rand.Chance(chance);
                    if (splash) other.Notify_Intercepted(by);
                    AirDefenseHooks.WorldEvent(AirCombatEventKind.InterceptResolved, other, by, site, site?.Map,
                        hitChance: chance, outcome: splash ? AirInterceptOutcome.Hit : AirInterceptOutcome.Miss, secondary: true);
                }
            }
            drawer.AddFlash(AirTrackDisplay.Raised(point, lift));
        }

        private void Resolve(WorldObject_Interceptor interceptor)
        {
            IAirDefenseTrack target = TrackOf(interceptor.target);
            WorldObject targetObject = target?.Self;
            float meetLift = interceptor.MeetLift; // 命中會移除目標，先記下閃光的高度。
            MapParent site = interceptor.launchNode;
            bool valid = target != null && !targetObject.Destroyed && target.TicksToArrival > 0
                && target.Destination == interceptor.targetDestination && target.RouteRevision == interceptor.targetRevision;
            // 結算前重查友軍身分：飛行中結盟就安全解除，不命中也不退款。
            bool friendly = target != null && target.Attacker != null && AirDefenseAlertPolicy.IsFriendly(interceptor.Faction, target.Attacker);
            if (valid && friendly)
            {
                if (interceptor.Faction == Faction.OfPlayer || target.Attacker == Faction.OfPlayer)
                    Messages.Message("FFF.AirAlert.InterceptorSafed".Translate(targetObject.Label), new GlobalTargetInfo(targetObject),
                        MessageTypeDefOf.NeutralEvent, historical: false);
                AirDefenseHooks.WorldEvent(AirCombatEventKind.InterceptResolved, target, interceptor.Faction, site, site?.Map,
                    hitChance: interceptor.hitChance, outcome: AirInterceptOutcome.Safed);
            }
            else if (valid)
            {
                RollShot(target, interceptor.Faction, site, interceptor.hitChance, interceptor.ExtraRolls, interceptor.end, meetLift);
            }
            else if (target != null && !targetObject.Destroyed)
            {
                if (interceptor.Faction == Faction.OfPlayer || target.Attacker == Faction.OfPlayer)
                    Messages.Message("FFF.BVR.World.Evaded".Translate(targetObject.Label), new GlobalTargetInfo(targetObject),
                        MessageTypeDefOf.NeutralEvent, historical: false);
                AirDefenseHooks.WorldEvent(AirCombatEventKind.InterceptResolved, target, interceptor.Faction, site, site?.Map,
                    hitChance: interceptor.hitChance, outcome: AirInterceptOutcome.Evaded);
            }
            if (target != null && target.State.interceptor == interceptor) target.State.interceptor = null;
            interceptor.Destroy();
        }

        /// <summary>開發工具：列出每條航跡被各節點接受或拒絕的理由，以及目前的封鎖圈。</summary>
        public string DevDescribeDecisions()
        {
            RebuildNodes();
            int now = Find.TickManager.TicksGame;
            int since = now - Mathf.Max(1, AirDefenseTuningDef.Current.scanIntervalTicks);
            var sb = new System.Text.StringBuilder();
            foreach (IAirDefenseTrack track in Tracks)
            {
                MapParent target = WorldComponent_FactionAirDefenseAlert.TargetSite(track);
                sb.AppendLine($"{track.Self.Label} #{track.Self.ID}: attacker={track.Attacker?.Name ?? "unknown"}, target={target?.Label ?? track.Destination.ToString()} [{target?.Faction?.Name ?? "-"}], eta={track.TicksToArrival}, status={track.State.StatusKey.Translate()}");
                foreach (AirDefenseObservation o in track.State.observations)
                    sb.AppendLine($"    intel {o.faction?.Name}: last seen {now - o.lastSeenTick} ticks ago, reported sites {o.reportedSites.Count}");
                foreach (AirDefenseNode n in nodes)
                {
                    if (n.parent == null || n.faction == null) { continue; }
                    string verdict = !n.CanReachAltitude(track.AltitudeAt(0)) ? "outside intercept altitude"
                        : !n.MayObserve(track) ? "ignores (same faction / ally)"
                        : !n.PolicyAllows(track) ? "observe only (stage policy)"
                        : !track.State.FactionSees(n.faction, since) ? "no fresh intel"
                        : !n.CanEngage(track) ? "outside coverage"
                        : !n.HasInterceptorAmmo ? "no interceptors"
                        : "may engage";
                    sb.AppendLine($"    {n.parent.Label} [{n.faction.Name}] stage={(n.UsesAlertRules ? n.alertStage.ToString() : "legacy")} range={n.DetectionRange(track.TargetProps, track):0.#}: {verdict}");
                }
            }
            foreach (BlockadeZone zone in AirspaceRestrictionUtility.ActiveZones)
                sb.AppendLine($"Blockade: {zone.site.Label} [{zone.faction.Name}] radius {zone.radius:0.#} tiles on {zone.tile.LayerDef.defName}");
            return sb.Length == 0 ? "No world tracks or blockades." : sb.ToString();
        }

        /// <summary>空爆波及：玩家（或警戒系統關閉時）只波及敵對導彈；NPC 波及非盟友導彈。</summary>
        private static bool WouldAirburst(Faction by, IAirDefenseTrack other)
        {
            if (by == null) { return false; }
            if (by.IsPlayer || !WorldComponent_FactionAirDefenseAlert.Enabled) { return by.HostileTo(other.Attacker); }
            return AirDefenseAlertPolicy.IsNonAlly(by, other.Attacker);
        }
        /// <summary>
        /// 每 60 ticks 取樣所有 MapParent 的 tile，推導移動速度。
        /// 只觀測、不 patch 原版，因此轉移艦、會搬遷的 NPC 陣地與其他 mod 的移動 MapParent 都涵蓋。
        /// </summary>
        private void SampleMovement(int now)
        {
            if (lastMotionSampleTick >= 0 && now - lastMotionSampleTick < MotionSampleIntervalTicks) { return; }
            int elapsed = lastMotionSampleTick < 0 ? MotionSampleIntervalTicks : Mathf.Max(1, now - lastMotionSampleTick);
            lastMotionSampleTick = now;

            foreach (MapParent parent in Find.WorldObjects.AllWorldObjects.OfType<MapParent>())
            {
                if (parent == null || parent.Destroyed) { continue; }
                int id = parent.ID;
                motion.TryGetValue(id, out MotionSample sample);
                if (sample.tile.Valid && parent.Tile.Valid && sample.tile != parent.Tile)
                {
                    float tiles = MissileWorldUtility.SurfaceTilesBetween(
                        MissileWorldUtility.TileCenter(sample.tile), MissileWorldUtility.TileCenter(parent.Tile));
                    sample.tilesPerTick = tiles / elapsed;
                    sample.tick = now;
                }
                sample.tile = parent.Tile;
                sample.lastSeen = now;
                motion[id] = sample;
            }

            // 清掉已消失的據點，避免字典隨長局無限成長。
            if (motion.Count > 0)
            {
                List<int> stale = null;
                foreach (KeyValuePair<int, MotionSample> pair in motion)
                {
                    if (now - pair.Value.lastSeen > MotionMemoryTicks)
                    {
                        stale ??= new List<int>();
                        stale.Add(pair.Key);
                    }
                }
                if (stale != null)
                {
                    foreach (int id in stale) { motion.Remove(id); }
                }
            }
        }

        /// <summary>該據點是否正在移動（最近 <see cref="MotionMemoryTicks"/> 內觀測到 tile 變化）。</summary>
        public bool IsMobileTarget(MapParent parent)
        {
            if (parent == null) { return false; }
            if (!motion.TryGetValue(parent.ID, out MotionSample sample)) { return false; }
            return sample.tilesPerTick > 0.00001f
                && Find.TickManager.TicksGame - sample.tick <= MotionMemoryTicks;
        }

        /// <summary>移動目標的觀測速度（地表格／tick）；靜止或未知為 0。</summary>
        public float MobileTilesPerTick(MapParent parent)
            => parent != null && motion.TryGetValue(parent.ID, out MotionSample sample) ? sample.tilesPerTick : 0f;

        /// <summary>
        /// 箔條／干擾絲：只在「已被火控鎖定」時觸發，成功則斷鎖並延後重新鎖定；
        /// 不論成敗都消耗一次，次數用完就不再作用（飛行中不補充）。
        /// </summary>
        private static void TryExpendChaff(IAirDefenseTrack track, int now)
        {
            AirTrackState state = track.State;
            if (state.chaffLeft == 0) { return; }
            if (state.chaffLeft < 0)
            {
                state.chaffLeft = track.Config?.ChaffCharges ?? 0;
                if (state.chaffLeft <= 0) { return; }
            }
            if (state.lockUntil < 0 || state.lockNode == null || state.lockFaction == null) { return; }
            float jam = track.Config?.JamStrength ?? 0f;
            if (jam <= 0f) { return; }

            state.chaffLeft--;
            state.jamCount++;
            float chance = Mathf.Clamp01(AirDefenseTuningDef.Current.chaffBreakChance * jam);
            if (!Rand.Chance(chance)) { return; }

            state.ClearLock();
            state.lockDelayUntil = now + AirDefenseTuningDef.Current.chaffLockDelayTicks;
            if (track.Attacker == Faction.OfPlayer || state.detectedByFaction == Faction.OfPlayer)
            {
                Messages.Message("FFF.BVR.World.Chaff".Translate(track.Self.Label), new GlobalTargetInfo(track.Self),
                    MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        public override void WorldComponentUpdate()
        {
            base.WorldComponentUpdate();
            if (WorldRendererUtility.WorldSelected)
            {
                drawer.Draw(Nodes, ShowAllCoverage);
                drawer.DrawAltitudeTracks(Tracks);
            }
        }
    }
}
