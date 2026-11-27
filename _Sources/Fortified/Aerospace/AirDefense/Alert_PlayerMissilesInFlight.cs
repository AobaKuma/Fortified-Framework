using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 玩家作為攻擊方、導彈正在被敵方 BVR 系統追蹤時顯示。<br/>
    /// ‧ Hover 展開 Tooltip：每枚導彈的類型、落點座標、攔截狀態與預計衝擊倒計時。<br/>
    /// ‧ 左鍵循環：依次把相機跳到各枚導彈的落點（支援跨地圖切換）。<br/>
    /// ‧ 右鍵反向循環。
    /// </summary>
    [FFF_Feature(FFF_AerospaceFeatures.Missiles)]
    public class Alert_PlayerMissilesInFlight : Alert
    {
        // 相機循環索引（跨幀持久）。
        private int jumpIndex = -1;

        // ── 資料蒐集 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 蒐集所有由玩家發射、目前正被敵方 BVR 系統追蹤的 <see cref="AirDefenseTarget"/>。
        /// 依波次衝擊時間升序排列。
        /// </summary>
        private static List<(AirDefenseWave wave, AirDefenseTarget target, Map map)> GetPlayerTargets()
        {
            var result = new List<(AirDefenseWave wave, AirDefenseTarget target, Map map)>();
            foreach (Map map in Find.Maps)
            {
                MapComponent_AirDefense comp = map.GetComponent<MapComponent_AirDefense>();
                if (comp == null) continue;

                foreach (AirDefenseWave wave in comp.Waves)
                {
                    // 防禦方不是玩家 → 玩家正在攻擊此地圖。
                    // 沒有任何防空的一方也照樣回報盤旋中的飛彈（玩家需要知道自己的無人機還在等目標）。
                    if (wave.defenderFaction != null && wave.defenderFaction.IsPlayer) continue;
                    if (wave.defenderFaction == null && !wave.loitering) continue;

                    foreach (AirDefenseTarget target in wave.targets)
                    {
                        // 確認是玩家發射的導彈（IIncomingMissile）
                        if (target.Missile is IIncomingMissile mi && mi.Attacker?.IsPlayer == true)
                            result.Add((wave, target, map));
                    }
                }
            }
            result.Sort((a, b) => a.wave.tickToImpact.CompareTo(b.wave.tickToImpact));
            return result;
        }

        // ── Alert 介面 ────────────────────────────────────────────────────────

        private static List<WorldObject_Missile> WorldTargets() => WorldComponent_AirDefenseNetwork.Current?.MissileTracks
            .Where(t => t.Attacker == Faction.OfPlayer).OrderBy(t => t.TicksToArrival).ToList()
            ?? new List<WorldObject_Missile>();

        public override AlertReport GetReport()
        {
            if (WorldTargets().Count > 0) return AlertReport.Active;
            // 短路版本：找到第一個匹配即返回，避免建立完整列表。
            foreach (Map map in Find.Maps)
            {
                MapComponent_AirDefense comp = map.GetComponent<MapComponent_AirDefense>();
                if (comp == null) continue;
                foreach (AirDefenseWave wave in comp.Waves)
                {
                    if (wave.defenderFaction != null && wave.defenderFaction.IsPlayer) continue;
                    if (wave.defenderFaction == null && !wave.loitering) continue;
                    foreach (AirDefenseTarget target in wave.targets)
                    {
                        if (target.Missile is IIncomingMissile mi && mi.Attacker?.IsPlayer == true)
                            return AlertReport.Active;
                    }
                }
            }
            return AlertReport.Inactive;
        }

        /// <summary>一個打擊波次的摘要：枚數、ETA 區間、被偵測與被接戰的枚數。</summary>
        public static string StrikeWaveLine(List<WorldObject_OutgoingMissile> tracks)
        {
            WorldObject_OutgoingMissile first = tracks[0];
            int detected = tracks.Count(t => t.State.detectedByFaction != null);
            int engaged = tracks.Count(t => t.State.lockUntil >= 0 || t.State.interceptor != null);
            return "FFF.Strike.Alert.WaveLine".Translate(WorldComponent_StrikeReports.MissionLabel(first.strikeMissionId), first.strikeWave + 1, tracks.Count,
                tracks.Min(t => t.TicksToArrival).ToStringTicksToPeriod(), tracks.Max(t => t.TicksToArrival).ToStringTicksToPeriod(),
                detected, engaged);
        }

        public override string GetLabel()
        {
            var targets = GetPlayerTargets();
            var world = WorldTargets();
            if (targets.Count == 0 && world.Count == 0) return base.GetLabel();
            if (world.Count > 0)
            {
                int eta = world[0].TicksToArrival;
                if (targets.Count > 0) eta = System.Math.Min(eta, targets.Min(t => t.wave.tickToImpact) - Find.TickManager.TicksGame);
                return "FFF.Alert.PlayerMissiles.Label".Translate(targets.Count + world.Count, eta.ToStringTicksToPeriod());
            }

            int now = Find.TickManager.TicksGame;
            int earliestTick = targets.Min(t => t.wave.tickToImpact);
            int timeLeft = earliestTick - now;

            return "FFF.Alert.PlayerMissiles.Label".Translate(
                targets.Count,
                timeLeft.ToStringTicksToPeriod(allowSeconds: false));
        }

        public override TaggedString GetExplanation()
        {
            var targets = GetPlayerTargets();
            var world = WorldTargets();
            if (targets.Count == 0 && world.Count == 0) return base.GetExplanation();

            int now = Find.TickManager.TicksGame;
            var sb = new StringBuilder();
            // 波次打擊的導彈依任務／波次彙整成一行，其餘航跡逐一列出
            foreach (var group in world.OfType<WorldObject_OutgoingMissile>().Where(t => t.strikeMissionId >= 0)
                .GroupBy(t => (t.strikeMissionId, t.strikeWave)).OrderBy(g => g.Min(t => t.TicksToArrival)))
            {
                sb.AppendLine(StrikeWaveLine(group.ToList()));
            }
            // 不屬於打擊任務的分導群組：整組彙整成一行，不逐一列出每枚子彈（計畫 §11）。
            var loose = world.Where(t => !(t is WorldObject_OutgoingMissile s && s.strikeMissionId >= 0)).ToList();
            foreach (var group in loose.OfType<WorldObject_Missile>().Where(t => t.mirvGroupId >= 0)
                .GroupBy(t => t.mirvGroupId).OrderBy(g => g.Min(t => t.TicksToArrival)))
            {
                List<WorldObject_Missile> members = group.ToList();
                sb.AppendLine("FFF.Mirv.AlertLine".Translate(group.Key, members.Count,
                    members.Count(m => m.mirvRole == MirvRole.Warhead), members.Count(m => m.mirvRole == MirvRole.Decoy),
                    members.Min(m => m.TicksToArrival).ToStringTicksToPeriod(), members.Max(m => m.TicksToArrival).ToStringTicksToPeriod()));
            }
            foreach (var track in loose.Where(t => !(t is WorldObject_Missile m && m.mirvGroupId >= 0)))
                sb.AppendLine(track.Label + "\n" + track.GetInspectString() + "\n");

            // ── 按目標地圖分組 ──
            var byMap = new Dictionary<Map, List<(AirDefenseWave wave, AirDefenseTarget target)>>();
            foreach (var (wave, target, map) in targets)
            {
                if (!byMap.TryGetValue(map, out var list))
                    byMap[map] = list = new List<(AirDefenseWave wave, AirDefenseTarget target)>();
                list.Add((wave, target));
            }

            foreach (var kv in byMap)
            {
                Map map = kv.Key;
                string mapLabel = map.Parent?.LabelCap ?? map.ToString();
                sb.AppendLine("FFF.Alert.PlayerMissiles.MapHeader".Translate(mapLabel));

                // ── 按波次分組，並依衝擊時間升序 ──
                var byWave = new Dictionary<AirDefenseWave, List<AirDefenseTarget>>();
                foreach (var (wave, target) in kv.Value)
                {
                    if (!byWave.TryGetValue(wave, out var wl))
                        byWave[wave] = wl = new List<AirDefenseTarget>();
                    wl.Add(target);
                }

                foreach (var waveKv in byWave.OrderBy(w => w.Key.tickToImpact))
                {
                    AirDefenseWave wave = waveKv.Key;
                    int timeLeft = wave.tickToImpact - now;
                    // 盤旋飛控：導彈在上空等待目標，不會「衝擊」，改報盤旋剩餘時間。
                    sb.AppendLine(wave.loitering
                        ? "FFF.Alert.Pods.Window.Loitering".Translate(
                            Mathf.Max(0, wave.loiterUntil - now).ToStringTicksToPeriod(allowSeconds: false))
                        : "FFF.Alert.PlayerMissiles.WaveImpact".Translate(
                            timeLeft.ToStringTicksToPeriod(allowSeconds: false)));

                    foreach (AirDefenseTarget target in waveKv.Value)
                    {
                        // 攔截狀態
                        string statusKey;
                        if (wave.loitering)
                            statusKey = "FFF.Alert.Pods.Status.Loitering";
                        else if (target.midcourseEngagedUntil > now)
                            statusKey = "FFF.Alert.Pods.Status.InFlight";
                        else if (target.lockUntil > now)
                            statusKey = "FFF.Alert.Pods.Status.Locking";
                        else
                            statusKey = "FFF.Alert.Pods.Status.Unengaged";

                        // 導彈名稱
                        string missileLabel = target.Payload?.Label
                            ?? target.Payload?.def?.label
                            ?? "?";

                        sb.AppendLine("FFF.Alert.PlayerMissiles.Target".Translate(
                            missileLabel,
                            target.position.x,
                            target.position.z,
                            statusKey.Translate()));
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine("FFF.Alert.PlayerMissiles.ClickHint".Translate());
            return sb.ToString().TrimEnd();
        }

        protected override void OnClick()
        {
            var targets = GetPlayerTargets();
            var world = WorldTargets();
            int count = targets.Count + world.Count;
            if (count == 0) return;
            jumpIndex = GenMath.PositiveMod(jumpIndex + (Event.current.button == 1 ? -1 : 1), count);
            if (jumpIndex < world.Count)
                CameraJumper.TryJump(new GlobalTargetInfo(world[jumpIndex]));
            else
            {
                var (_, target, map) = targets[jumpIndex - world.Count];
                CameraJumper.TryJump(target.position, map);
            }
        }
    }
}
