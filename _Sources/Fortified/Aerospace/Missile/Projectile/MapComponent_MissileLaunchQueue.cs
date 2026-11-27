using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>一枚等待生成的導彈（發射邏輯已完成，只差畫面上的升空彈）。</summary>
    public class PendingMissileLaunch : IExposable
    {
        public int tick;
        public ThingDef def;
        public IntVec3 cell;
        public Rot4 rotation;
        public bool hasLaunchAngle;
        public float launchAngle;
        public Vector3 launchOffset;
        public bool hasLaunchOffset;
        public Thing launcher;

        /// <summary>存檔時發射架已被摧毀（引用存不下來，讀檔後 <see cref="launcher"/> 為 null）。</summary>
        public bool launcherLost;

        // 對地離場
        public WorldObject worldObject;

        // 攔截
        public bool interceptor;
        public int targetId = -1;
        public float hitChance;

        public bool hasHeading;
        public float heading;

        /// <summary>發射架有井蓋、下令時還沒全開：等井蓋全開才升空（不佔排程時段，到時再補間隔）。</summary>
        public bool holdForHatch;

        /// <summary>
        /// 人工瞄準中（<see cref="MissileLauncherExtension.aimTicks"/>）：砲頭轉向 <see cref="heading"/>，到 <see cref="tick"/> 才升空；
        /// 操作員不在時暫停。導彈在這段期間仍畫在發射架上（<see cref="roundDef"/>）。
        /// </summary>
        public bool holdForAim;

        /// <summary>排入時要求的瞄準時間（只在 <see cref="MapComponent_MissileLaunchQueue.Request"/> 使用，不存檔）。</summary>
        public int aimTicks;

        /// <summary>這一發的導彈物品 def：瞄準期間由 <see cref="CompTurretFacingRenderer"/> 畫在發射架上。</summary>
        public ThingDef roundDef;

        public float? Heading => hasHeading ? heading : (float?)null;

        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref rotation, "rotation");
            Scribe_Values.Look(ref hasLaunchAngle, "hasLaunchAngle", false);
            Scribe_Values.Look(ref launchAngle, "launchAngle", 0f);
            Scribe_Values.Look(ref launchOffset, "launchOffset");
            Scribe_Values.Look(ref hasLaunchOffset, "hasLaunchOffset", false);
            if (Scribe.mode == LoadSaveMode.Saving && launcher != null && launcher.Destroyed) { launcher = null; launcherLost = true; }
            Scribe_References.Look(ref launcher, "launcher");
            Scribe_Values.Look(ref launcherLost, "launcherLost", false);
            Scribe_Deep.Look(ref worldObject, "worldObject");
            Scribe_Values.Look(ref interceptor, "interceptor", false);
            Scribe_Values.Look(ref targetId, "targetId", -1);
            Scribe_Values.Look(ref hitChance, "hitChance", 0f);
            Scribe_Values.Look(ref hasHeading, "hasHeading", false);
            Scribe_Values.Look(ref heading, "heading", 0f);
            Scribe_Values.Look(ref holdForHatch, "holdForHatch", false);
            Scribe_Values.Look(ref holdForAim, "holdForAim", false);
            Scribe_Defs.Look(ref roundDef, "roundDef");
        }
    }

    /// <summary>
    /// 導彈發射排程：同一張地圖的升空彈至少相隔 <see cref="SpacingTicks"/> 生成，不會在同一 tick 一起竄出
    /// （多座發射架同波齊射、ripple 續射、多具 SAM 同時接戰、多選建築一起按發射）。
    ///
    /// 只錯開「畫面上的升空彈」：彈藥消耗、冷卻、BVR 通道、世界航跡物件的建立都在發射當下完成；
    /// 對地彈的世界航跡在升空彈飛出地圖時才加入世界，所以會跟著延後同樣的 tick（打擊時間軸以
    /// <see cref="WorldObject_OutgoingMissile.launchSpawnTick"/> 計入）。發射後焰在升空彈實際生成時才觸發。
    /// 等待中的發射隨地圖存檔。
    /// </summary>
    [FFF_Feature(FFF_AerospaceFeatures.Missiles)]
    public class MapComponent_MissileLaunchQueue : MapComponent
    {
        /// <summary>同一張地圖兩枚升空彈的最小間隔（tick）。</summary>
        public const int SpacingTicks = 15;

        private int lastSlotTick = -100000;
        private List<PendingMissileLaunch> pending = new List<PendingMissileLaunch>();

        public MapComponent_MissileLaunchQueue(Map map) : base(map) { }

        public static MapComponent_MissileLaunchQueue For(Map map) => map?.GetComponent<MapComponent_MissileLaunchQueue>();

        /// <summary>經排程發射（地圖沒有排程元件時立刻生成）；回傳實際生成 tick。</summary>
        public static int Launch(Map map, PendingMissileLaunch launch)
        {
            MapComponent_MissileLaunchQueue queue = For(map);
            if (queue != null)
            {
                queue.Request(launch, out int spawnTick);
                return spawnTick;
            }
            queue = new MapComponent_MissileLaunchQueue(map);
            launch.tick = Find.TickManager.TicksGame;
            queue.Spawn(launch);
            return launch.tick;
        }

        /// <summary>等待生成的對地離場彈數。</summary>
        public int PendingDepartures => pending.Count(p => !p.interceptor);
        public int PendingCount => pending.Count;

        /// <summary>下一個可用的生成 tick（不保留）。</summary>
        public int NextSlot(int now) => Mathf.Max(now, lastSlotTick + SpacingTicks);

        /// <summary>排入一枚導彈：時段空著就立刻生成並回傳升空彈，否則排到下一個時段、回傳 null。回傳的 tick 為實際生成時刻。</summary>
        public Thing Request(PendingMissileLaunch launch, out int spawnTick)
        {
            int now = Find.TickManager.TicksGame;
            // 發射井：井蓋還沒全開就先開蓋，升空彈等全開才生成（預估時刻回報給打擊時間軸）。
            CompSiloHatch hatch = launch.interceptor ? null : CompSiloHatch.For(launch.launcher);
            int hold = hatch?.TicksUntilOpen ?? 0;
            if (hold > 0) { launch.holdForHatch = true; }
            // 人工瞄準：與井蓋同樣先扣住，不佔排程時段，到時再補最小間隔。
            if (launch.aimTicks > 0)
            {
                launch.holdForAim = true;
                hold = Mathf.Max(hold, launch.aimTicks);
            }
            if (hold > 0)
            {
                spawnTick = Mathf.Max(now + hold, lastSlotTick + SpacingTicks);
                launch.tick = spawnTick;
                pending.Add(launch);
                return null;
            }
            spawnTick = NextSlot(now);
            lastSlotTick = spawnTick;
            launch.tick = spawnTick;
            if (spawnTick <= now) { return Spawn(launch); }
            pending.Add(launch);
            return null;
        }

        public override void MapComponentTick()
        {
            if (pending.Count == 0) { return; }
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < pending.Count; i++)
            {
                PendingMissileLaunch launch = pending[i];
                if (launch.holdForAim && !DepartureLost(launch)) { AimTick(launch, now); }
                if (launch.tick > now) { continue; }
                if ((launch.holdForHatch || launch.holdForAim) && !DepartureLost(launch))
                {
                    CompSiloHatch hatch = launch.holdForHatch ? CompSiloHatch.For(launch.launcher) : null;
                    if (hatch != null && !hatch.FullyOpen)
                    {
                        hatch.EnsureLaunchOpen();
                        continue;
                    }
                    if (launch.holdForAim) { FinishAim(launch); }
                    // 井蓋開好了／瞄準完成，但別的發射剛升空：補上最小間隔。
                    if (now < lastSlotTick + SpacingTicks)
                    {
                        launch.tick = lastSlotTick + SpacingTicks;
                        continue;
                    }
                    lastSlotTick = now;
                }
                pending.RemoveAt(i);
                i--;
                Spawn(launch);
            }
        }

        private Thing Spawn(PendingMissileLaunch launch)
        {
            if (launch.def == null) { return null; }
            if (!launch.interceptor && DepartureLost(launch))
            {
                // 排隊等升空的期間發射架被摧毀、拆除或易主：導彈沒離架，不能再從原地「憑空」升空。
                // 世界航跡還沒加入世界，直接銷毀；陣地的火力通道與打擊任務的欄位都以 Destroyed 判定它已結束。
                if (launch.worldObject != null && !launch.worldObject.Destroyed) { launch.worldObject.Destroy(); }
                if (launch.launcher != null && !launch.launcher.Destroyed) { CompSiloHatch.For(launch.launcher)?.Notify_LaunchSpawned(); }
                if (Prefs.DevMode)
                    Log.Message($"[Fortified LaunchQueue] 發射架已失去，取消一枚排隊中的 {launch.def.defName}。");
                return null;
            }
            Thing launcher = launch.launcher != null && !launch.launcher.Destroyed ? launch.launcher : null;
            if (launcher is ThingWithComps building && building.Spawned && building.Map == map)
            {
                CompLaunchBackblast.TryTrigger(building);
                if (!launch.interceptor) { CompSiloHatch.For(building)?.Notify_LaunchSpawned(); }
            }
            IntVec3 cell = launch.cell.InBounds(map) ? launch.cell : launch.cell.ClampInsideMap(map);
            return launch.interceptor
                ? MissileProjectileMaker.LaunchInterceptor(launch.def, cell, map, launch.rotation, launcher, launch.targetId, launch.hitChance,
                    launch.Heading, launch.hasLaunchAngle ? launch.launchAngle : (float?)null,
                    launch.hasLaunchOffset ? launch.launchOffset : Vector3.zero)
                : MissileProjectileMaker.LaunchDeparture(launch.def, cell, map, launch.rotation, launcher, launch.worldObject,
                    launch.Heading, launch.hasLaunchAngle ? launch.launchAngle : (float?)null,
                    launch.hasLaunchOffset ? launch.launchOffset : Vector3.zero);
        }

        /// <summary>人工瞄準中：砲頭轉向出發方位；操作員不在（砲頭不運作）時瞄準暫停，升空時刻跟著順延。</summary>
        private static void AimTick(PendingMissileLaunch launch, int now)
        {
            CompTurretFacingRenderer facing = (launch.launcher as ThingWithComps)?.GetComp<CompTurretFacingRenderer>();
            if (facing == null) { return; }
            if (facing.Props.requireActive && !facing.DeviceActive)
            {
                if (launch.tick >= now) { launch.tick++; }
                return;
            }
            if (launch.hasHeading)
            {
                facing.ForceFaceTarget(launch.launcher.DrawPos + MissileMapEntryUtility.HeadingVector(launch.heading) * 10f, 2);
            }
        }

        /// <summary>瞄準完成：升空角度與離架位置改用砲頭現在的角度（下令時記下的是瞄準前的角度）。</summary>
        private void FinishAim(PendingMissileLaunch launch)
        {
            CompTurretFacingRenderer facing = (launch.launcher as ThingWithComps)?.GetComp<CompTurretFacingRenderer>();
            if (facing == null) { return; }
            int slot = AimingRoundsFor(launch.launcher).IndexOf(launch);
            if (slot >= 0 && facing.TryGetAimingRoundOffset(slot, out Vector3 offset))
            {
                launch.launchOffset = offset;
                launch.hasLaunchOffset = true;
            }
            launch.launchAngle = facing.CurRotation;
            launch.hasLaunchAngle = true;
        }

        /// <summary>這座發射架正在瞄準、還沒升空的導彈（依排入順序）。</summary>
        public List<PendingMissileLaunch> AimingRoundsFor(Thing launcher)
        {
            List<PendingMissileLaunch> result = new List<PendingMissileLaunch>();
            if (launcher == null) { return result; }
            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].holdForAim && pending[i].launcher == launcher) { result.Add(pending[i]); }
            }
            return result;
        }

        /// <summary>這座發射架瞄準中、還沒升空的導彈數（不配置清單；彈架容量判定每 tick 會呼叫多次）。</summary>
        public static int AimingCountFor(Thing launcher)
        {
            if (launcher == null || !launcher.Spawned) { return 0; }
            MapComponent_MissileLaunchQueue queue = For(launcher.Map);
            if (queue == null || queue.pending.Count == 0) { return 0; }
            int n = 0;
            for (int i = 0; i < queue.pending.Count; i++)
            {
                if (queue.pending[i].holdForAim && queue.pending[i].launcher == launcher) { n++; }
            }
            return n;
        }

        /// <summary>這座發射架最晚一發瞄準完成還要多久（沒有瞄準中的發射時為 0）。</summary>
        public int AimTicksLeft(Thing launcher, int now)
        {
            int left = 0;
            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].holdForAim && pending[i].launcher == launcher) { left = Mathf.Max(left, pending[i].tick - now); }
            }
            return left;
        }

        /// <summary>對地離場的發射架已不在本圖運作：被摧毀、被拆除／收起，或已不屬於發射方陣營（被佔領）。</summary>
        private bool DepartureLost(PendingMissileLaunch launch)
        {
            if (launch.launcherLost) { return true; }
            Thing launcher = launch.launcher;
            if (launcher == null) { return false; }   // 本來就沒有發射架的發射（例如測試／腳本）照常升空
            if (launcher.Destroyed || !launcher.Spawned || launcher.Map != map) { return true; }
            Faction side = launch.worldObject?.Faction;
            return side != null && launcher.Faction != side;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastSlotTick, "lastSlotTick", -100000);
            Scribe_Collections.Look(ref pending, "pending", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && pending == null) { pending = new List<PendingMissileLaunch>(); }
        }
    }
}
