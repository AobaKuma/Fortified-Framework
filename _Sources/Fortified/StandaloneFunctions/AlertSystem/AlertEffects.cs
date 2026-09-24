using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Fortified
{
    // ════════════════════════════════════════════════════════════
    //  一、地下設施封鎖：已改為中控建築驅動，見 Lockdown/MapComponent_FacilityLockdown.cs
    //  Facility lockdown now runs off a controller building; see Lockdown/MapComponent_FacilityLockdown.cs
    // ════════════════════════════════════════════════════════════
    /// <summary>
    /// 舊版 AlertEffectWorker_FacilityLockdown 的驅動器。舊版已移除；這個空殼只為了讓舊存檔裡的
    /// WorldComponent 能正常讀取，下下個版本刪除。
    /// Driver of the removed AlertEffectWorker_FacilityLockdown. This empty shell only keeps older saves
    /// loading cleanly; remove it two versions from now.
    /// </summary>
    [System.Obsolete("Facility lockdown is now driven by CompFacilityLockdownController; this shell only keeps old saves loading.")]
    public class WorldComponent_AlertLockdownDriver : WorldComponent
    {
        public WorldComponent_AlertLockdownDriver(World world) : base(world) { }
    }

    // ════════════════════════════════════════════════════════════
    //  二、空域封鎖
    //  AlertEffect_AirspaceBlockade
    // ════════════════════════════════════════════════════════════
    /// <summary>
    /// 警戒值滿後觸發空域封鎖：
    /// <list type="bullet">
    ///   <item>每 <see cref="strikeIntervalTicks"/>（預設 30 秒）對露天目標派出空襲掃射。</item>
    ///   <item>封鎖期間禁用穿梭機起飛（透過 <see cref="Patch_DisableShuttleLaunch"/>）。</item>
    ///   <item>持續 <see cref="durationTicks"/>（預設 2 小時）後解除。</item>
    /// </list>
    /// </summary>
    public class AlertEffectWorker_AirspaceBlockade : AlertEffectWorker
    {
        // ── 可配置 ──────────────────────────────────────────────
        /// <summary>空襲週期（ticks），預設 30 秒 = 1800。</summary>
        public int strikeIntervalTicks = 1800;
        /// <summary>封鎖持續時間（ticks），預設 2 小時。</summary>
        public int durationTicks = GenDate.TicksPerHour * 2;
        /// <summary>空襲使用的 IncidentDef，建議指向空中掃射 Incident。</summary>
        public IncidentDef strikeIncident;
        /// <summary>使用的派系 Def（null 則使用最近敵對派系）。</summary>
        public FactionDef strikeFactionDef;

        // ── 狀態 ────────────────────────────────────────────────
        private bool active = false;
        private int endTick;
        private int nextStrikeTick;
        private Map targetMap;

        public bool IsAirspaceBlocked => active && targetMap != null && Find.Maps.Contains(targetMap);

        public override void Execute(Map map)
        {
            if (map == null) return;
            targetMap = map;
            active = true;
            endTick = Find.TickManager.TicksGame + durationTicks;
            nextStrikeTick = Find.TickManager.TicksGame + strikeIntervalTicks;

            Messages.Message("FFF_Alert_AirspaceBlockade_Start".Translate(), MessageTypeDefOf.ThreatBig);

            // 登記到 MapComponent 讓其驅動 Tick
            map.GetComponent<MapComponent_AlertCounter>()?
                .RegisterAirspaceBlockade(this);
        }

        /// <summary>由 MapComponent_AlertCounter 每 tick 呼叫。</summary>
        public void Tick()
        {
            if (!active) return;
            int now = Find.TickManager.TicksGame;

            if (now >= endTick)
            {
                active = false;
                Messages.Message("FFF_Alert_AirspaceBlockade_End".Translate(), MessageTypeDefOf.NeutralEvent);
                return;
            }

            if (now >= nextStrikeTick)
            {
                nextStrikeTick = now + strikeIntervalTicks;
                LaunchAirStrike();
            }
        }

        private void LaunchAirStrike()
        {
            if (targetMap == null || !Find.Maps.Contains(targetMap)) return;

            // 優先使用指定 Incident
            IncidentDef def = strikeIncident
                ?? DefDatabase<IncidentDef>.GetNamedSilentFail("FFF_AirStrikeStrafe");
            if (def == null) { Log.Warning("[FFF] AirspaceBlockade: no strikeIncident defined."); return; }

            Faction faction = null;
            if (strikeFactionDef != null)
                faction = Find.FactionManager.FirstFactionOfDef(strikeFactionDef);
            faction ??= Find.FactionManager.AllFactions
                .Where(f => f.HostileTo(Faction.OfPlayer) && !f.def.hidden)
                .RandomElementWithFallback();

            IncidentParms parms = StorytellerUtility.DefaultParmsNow(def.category, targetMap);
            parms.target = targetMap;
            parms.faction = faction;
            parms.forced = true;

            if (def.Worker.CanFireNow(parms))
                def.Worker.TryExecute(parms);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  三、精確砲擊
    //  AlertEffect_PrecisionBombardment
    // ════════════════════════════════════════════════════════════
    /// <summary>
    /// 警戒值滿後觸發精確砲擊：
    /// 以 10~30 秒的隨機間隔轟炸地圖上的露天目標，
    /// 持續 <see cref="durationTicks"/>（預設 1 小時）後停止。
    /// </summary>
    public class AlertEffectWorker_PrecisionBombardment : AlertEffectWorker
    {
        // ── 可配置 ──────────────────────────────────────────────
        /// <summary>最短砲擊間隔（ticks），預設 600（10 秒）。</summary>
        public int minIntervalTicks = 600;
        /// <summary>最長砲擊間隔（ticks），預設 1800（30 秒）。</summary>
        public int maxIntervalTicks = 1800;
        /// <summary>持續時間（ticks），預設 1 小時。</summary>
        public int durationTicks = GenDate.TicksPerHour;
        /// <summary>砲擊使用的彈藥 ThingDef（預設使用遊戲內自帶砲彈）。</summary>
        public ThingDef shellDef;
        /// <summary>每次砲擊的齊射數量。</summary>
        public int shotsPerSalvo = 3;

        // ── 狀態 ────────────────────────────────────────────────
        private bool active = false;
        private int endTick;
        private int nextStrikeTick;
        private Map targetMap;

        public bool IsActive => active;

        public override void Execute(Map map)
        {
            if (map == null) return;
            targetMap = map;
            active = true;
            endTick = Find.TickManager.TicksGame + durationTicks;
            ScheduleNextStrike();

            Messages.Message("FFF_Alert_PrecisionBombardment_Start".Translate(), MessageTypeDefOf.ThreatBig);

            map.GetComponent<MapComponent_AlertCounter>()?
                .RegisterBombardment(this);
        }

        /// <summary>由 MapComponent_AlertCounter 每 tick 呼叫。</summary>
        public void Tick()
        {
            if (!active) return;
            int now = Find.TickManager.TicksGame;

            if (now >= endTick)
            {
                active = false;
                Messages.Message("FFF_Alert_PrecisionBombardment_End".Translate(), MessageTypeDefOf.NeutralEvent);
                return;
            }

            if (now >= nextStrikeTick)
            {
                ScheduleNextStrike();
                FireSalvo();
            }
        }

        private void ScheduleNextStrike()
        {
            nextStrikeTick = Find.TickManager.TicksGame
                + Rand.Range(minIntervalTicks, maxIntervalTicks);
        }

        private void FireSalvo()
        {
            if (targetMap == null || !Find.Maps.Contains(targetMap)) return;

            // 選取露天的敵方或玩家高價值目標
            List<Thing> candidates = targetMap.listerThings.AllThings
                .Where(t => t != null
                    && t.Spawned
                    && !t.Position.Roofed(targetMap)
                    && (t is Pawn p && p.Faction == Faction.OfPlayer
                        || t is Building b && b.Faction == Faction.OfPlayer))
                .ToList();

            if (candidates.Count == 0) return;

            ThingDef shell = shellDef
                ?? ThingDefOf.Shell_HighExplosive;

            for (int i = 0; i < shotsPerSalvo; i++)
            {
                Thing target = candidates.RandomElement();
                IntVec3 pos = target.Position;
                // 加入少量誤差，避免完全精確
                pos += new IntVec3(Rand.RangeInclusive(-2, 2), 0, Rand.RangeInclusive(-2, 2));
                if (!pos.InBounds(targetMap)) pos = target.Position;

                // 生成砲彈（直接在目標格爆炸）
                Projectile proj = (Projectile)GenSpawn.Spawn(
                    shell, pos, targetMap, WipeMode.Vanish);
                proj?.Launch(null, pos.ToVector3(), target, target,
                    ProjectileHitFlags.All, false, null, null);
            }
        }
    }

    // ════════════════════════════════════════════════════════════
    //  MapComponent_AlertCounter 擴充部分
    //  （透過 partial class 將持續效果 Tick 整合進 MapComponent）
    // ════════════════════════════════════════════════════════════
    public partial class MapComponent_AlertCounter
    {
        // ── 持續效果 Tick 支援 ───────────────────────────────────
        private AlertEffectWorker_AirspaceBlockade activeAirspaceBlockade;
        private AlertEffectWorker_PrecisionBombardment activeBombardment;

        public void RegisterAirspaceBlockade(AlertEffectWorker_AirspaceBlockade worker)
            => activeAirspaceBlockade = worker;

        public void RegisterBombardment(AlertEffectWorker_PrecisionBombardment worker)
            => activeBombardment = worker;

        /// <summary>是否正在執行空域封鎖（供 Shuttle 起飛 Patch 查詢）。</summary>
        public bool IsAirspaceBlocked
            => activeAirspaceBlockade?.IsAirspaceBlocked ?? false;

        // 在 MapComponentTick 追加 Tick（透過 partial 擴充）
        // 注意：partial 方法需在同檔案，這裡改用 hook 方式
        public void TickActiveEffects()
        {
            activeAirspaceBlockade?.Tick();
            activeBombardment?.Tick();
        }
    }

    // ════════════════════════════════════════════════════════════
    //  Patch：禁用穿梭機起飛（空域封鎖）
    // ════════════════════════════════════════════════════════════
    /// <summary>
    /// 空域封鎖期間禁止穿梭機起飛。
    /// 不使用 [HarmonyPatch] 自動掃描（HarmonyTargetMethod 回傳 null 會讓整個
    /// PatchAll 崩潰）；改由 HarmonyEntry 顯式呼叫 TryApply，方法不存在時靜默跳過。
    /// </summary>
    public static class Patch_DisableShuttleLaunch
    {
        /// <summary>
        /// 由 HarmonyEntry 呼叫。找到目標方法才套用 postfix，否則靜默略過。
        /// </summary>
        public static void TryApply(HarmonyLib.Harmony harmony)
        {
            const System.Reflection.BindingFlags bf =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

            // 新版：CanLaunch getter 返回 AcceptanceReport
            var canLaunch = typeof(CompShuttle).GetProperty("CanLaunch", bf)?.GetGetMethod();
            if (canLaunch != null)
            {
                harmony.Patch(canLaunch, postfix: new HarmonyLib.HarmonyMethod(
                    typeof(Patch_DisableShuttleLaunch), nameof(Postfix_Report)));
                return;
            }

            // 旧版：bool getter
            var boolGetter = FindLegacyBoolTarget(bf);
            if (boolGetter != null)
            {
                harmony.Patch(boolGetter, postfix: new HarmonyLib.HarmonyMethod(
                    typeof(Patch_DisableShuttleLaunch), nameof(Postfix_Bool)));
                return;
            }

            Log.Warning("[FFF] Patch_DisableShuttleLaunch: could not find patch target on CompShuttle. Airspace blockade shuttle prevention is disabled.");
        }

        // 旧版 bool getter 定位
        private static System.Reflection.MethodBase FindLegacyBoolTarget(System.Reflection.BindingFlags bf)
        {
            var getter = typeof(CompShuttle).GetProperty("IsBlocked", bf)?.GetGetMethod();
            if (getter != null) return getter;
            return typeof(CompShuttle).GetProperty("LoadingInProgressOrReadyToLaunch", bf)?.GetGetMethod();
        }

        // 判定空域封鎖中
        private static bool IsBlockedNow(CompShuttle shuttle)
        {
            Map map = shuttle.parent?.Map;
            if (map == null) return false;
            return map.GetComponent<MapComponent_AlertCounter>()?.IsAirspaceBlocked == true;
        }

        // 新版：封鎖期間拒絕起飛
        static void Postfix_Report(CompShuttle __instance, ref AcceptanceReport __result)
        {
            if (!__result.Accepted) return;
            if (IsBlockedNow(__instance))
                __result = "FFF_Alert_AirspaceBlockade_Start".Translate();
        }

        // 旧版：封鎖期間標記為受阻
        static void Postfix_Bool(CompShuttle __instance, ref bool __result)
        {
            if (__result) return;
            if (IsBlockedNow(__instance))
                __result = true;
        }
    }
}
