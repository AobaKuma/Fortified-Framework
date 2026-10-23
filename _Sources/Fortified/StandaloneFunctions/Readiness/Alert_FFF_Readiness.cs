using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified;

/// <summary>
/// 右側提示：列出戒備等級在 alertMinLevel 以上的派系、目前情勢、下一次降級的時間與戰爭目標。
/// 最高級或戰爭中轉紅。RimWorld 自動實例化，不需註冊。
///
/// Right-hand alert listing every faction at or above its alertMinLevel: the situation, time until the next drop and
/// the war target. Red at the top level or during a war. Vanilla instantiates it automatically.
/// </summary>
public class Alert_FFF_Readiness : Alert
{
    private readonly List<ReadinessState> active = new List<ReadinessState>();

    public Alert_FFF_Readiness()
    {
        defaultPriority = AlertPriority.Medium;
    }

    private bool Red
    {
        get
        {
            foreach (ReadinessState state in active)
            {
                if (state.AtWar || state.level >= state.def.MaxLevel) return true;
            }
            return false;
        }
    }

    protected override Color BGColor => Red ? Alert_Critical.BgColor() : Color.clear;

    public override AlertReport GetReport()
    {
        active.Clear();
        GameComponent_Readiness component = ReadinessUtility.Component;
        if (component == null) return AlertReport.Inactive;
        foreach (ReadinessState state in component.StatesForReading)
        {
            if (state.def.showAlert && state.level >= state.def.alertMinLevel && state.def.Worker.Faction != null) active.Add(state);
        }
        return active.Count > 0 ? AlertReport.Active : AlertReport.Inactive;
    }

    public override string GetLabel()
    {
        if (active.Count == 0) return string.Empty;
        // 只放最嚴重的一個，其餘在說明裡。Show the most severe one; the rest are in the explanation.
        ReadinessState top = active[0];
        foreach (ReadinessState state in active)
        {
            if ((float)state.level / Mathf.Max(1, state.def.MaxLevel) > (float)top.level / Mathf.Max(1, top.def.MaxLevel)) top = state;
        }
        string label = top.def.AlertLabel.Formatted(top.def.Worker.Faction.Name, top.def.LevelLabel(top.level)).Resolve();
        return active.Count > 1 ? label + " (+" + (active.Count - 1) + ")" : label;
    }

    public override TaggedString GetExplanation()
    {
        StringBuilder sb = new StringBuilder();
        int now = Find.TickManager.TicksGame;
        foreach (ReadinessState state in active)
        {
            if (sb.Length > 0) sb.AppendLine().AppendLine();
            Faction faction = state.def.Worker.Faction;
            sb.AppendLine(state.def.AlertExplanation.Formatted(faction.NameColored, state.def.LevelLabel(state.level)).Resolve());
            string description = state.def.Worker.LevelDescription(state.level);
            if (!description.NullOrEmpty()) sb.AppendLine(description);
            // 狀態（戰爭、降級倒數、壓力）併成一段，不要一句一行。Status (war, countdown, pressure) as one paragraph, not a line each.
            List<string> status = new List<string>();
            if (state.warTarget != null) status.Add("FFF_Readiness_AtWar".Translate(state.warTarget.NameColored).Resolve());
            if (state.nextDropTick > now)
            {
                status.Add("FFF_Readiness_DropIn".Translate((state.nextDropTick - now).ToStringTicksToPeriod(allowSeconds: false, shortForm: false, canUseDecimals: false)).Resolve());
            }
            if (state.pressure > 0f && state.level < state.def.MaxLevel)
            {
                status.Add("FFF_Readiness_Pressure".Translate((state.pressure / state.def.pressurePerLevel).ToStringPercent()).Resolve());
            }
            if (status.Count > 0)
            {
                // 中文句子之間不加空格。No spaces between CJK sentences.
                string joiner = LanguageDatabase.activeLanguage?.folderName?.StartsWith("Chinese") == true ? string.Empty : " ";
                sb.AppendLine().AppendLine(string.Join(joiner, status));
            }
        }
        return sb.ToString().TrimEndNewlines();
    }
}
