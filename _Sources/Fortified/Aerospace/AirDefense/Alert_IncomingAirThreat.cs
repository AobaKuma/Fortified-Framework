using RimWorld;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace Fortified
{
    [FFF_Feature(FFF_AerospaceFeatures.Missiles)]
    public class Alert_IncomingAirThreat : Alert
    {
        // 只提示由玩家防禦的波次（玩家自家基地防空）；玩家進攻敵方基地時，敵方防空不在此提示。
        // 取「最快抵達」的攔截窗口；短時間內的多個可攔截物已於登記時合併到同一波次。
        private static AirDefenseWave SoonestPlayerWave(Map map)
        {
            MapComponent_AirDefense comp = map != null ? map.GetComponent<MapComponent_AirDefense>() : null;
            if (comp == null) { return null; }

            AirDefenseWave best = null;
            foreach (AirDefenseWave w in comp.Waves)
            {
                if (w.defenderFaction == null || !w.defenderFaction.IsPlayer) { continue; }
                if (best == null || w.tickToImpact < best.tickToImpact) { best = w; }
            }
            return best;
        }

        public override string GetLabel()
        {
            AirDefenseWave wave = SoonestPlayerWave(Find.CurrentMap);
            if (wave != null)
            {
                int timeLeft = Mathf.Max(0, wave.tickToImpact - Find.TickManager.TicksGame);
                // 盤旋中的飛彈不會「抵達」，標題改報盤旋剩餘時間。
                if (wave.loitering)
                {
                    return "FFF.Alert.Pods.Loitering".Translate(wave.targets.Count, timeLeft.ToStringTicksToPeriod());
                }
                return "FFF.Alert.Pods".Translate(wave.targets.Count, timeLeft.ToStringTicksToPeriod());
            }
            return base.GetLabel();
        }

        public override TaggedString GetExplanation()
        {
            Map map = Find.CurrentMap;
            AirDefenseWave wave = SoonestPlayerWave(map);
            if (wave == null) { return base.GetExplanation(); }

            int now = Find.TickManager.TicksGame;
            int timeLeft = wave.tickToImpact - now;
            MapComponent_AirDefense comp = map.GetComponent<MapComponent_AirDefense>();
            int terminalWindow = comp != null ? comp.TerminalWindowTicks(wave.defenderFaction) : 0;
            int midcourseLeft = Mathf.Max(0, timeLeft - terminalWindow);

            StringBuilder sb = new StringBuilder();

            // ---- 攔截階段剩餘窗口 ----
            if (wave.loitering)
            {
                // 盤旋飛控：導引頭還找不到目標，飛彈在上空等待；盤旋期間不走中過程，全程只交給末端防禦接戰。
                sb.AppendLine("FFF.Alert.Pods.Window.Loitering".Translate(
                    Mathf.Max(0, wave.loiterUntil - now).ToStringTicksToPeriod()));
            }
            else
            {
                if (midcourseLeft > 0)
                {
                    sb.AppendLine("FFF.Alert.Pods.Window.Midcourse".Translate(
                        midcourseLeft.ToStringTicksToPeriod()));
                }
                if (terminalWindow > 0)
                {
                    sb.AppendLine("FFF.Alert.Pods.Window.Terminal".Translate(
                        Mathf.Min(timeLeft, terminalWindow).ToStringTicksToPeriod()));
                }
            }

            // ---- 逐目標列表 ----
            sb.AppendLine();
            foreach (AirDefenseTarget t in wave.targets)
            {
                string statusKey;
                if (wave.loitering)                      { statusKey = "FFF.Alert.Pods.Status.Loitering"; }
                else if (t.midcourseEngagedUntil > now)  { statusKey = "FFF.Alert.Pods.Status.InFlight"; }
                else if (t.lockUntil > now)              { statusKey = "FFF.Alert.Pods.Status.Locking"; }
                else                                     { statusKey = "FFF.Alert.Pods.Status.Unengaged"; }

                string label = t.DisplayLabel(timeLeft);

                sb.AppendLine("FFF.Alert.Pods.Target".Translate(label, statusKey.Translate()));
            }

            return sb.ToString().TrimEnd();
        }

        public override AlertReport GetReport()
        {
            return SoonestPlayerWave(Find.CurrentMap) != null ? AlertReport.Active : AlertReport.Inactive;
        }

        protected override void OnClick()
        {
            base.OnClick();
            Map map = Find.CurrentMap;
            AirDefenseWave wave = SoonestPlayerWave(map);
            if (wave != null && wave.targets.Any())
            {
                CameraJumper.TryJump(wave.targets.First().position, map);
            }
        }
    }
}
