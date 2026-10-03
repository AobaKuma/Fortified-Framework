using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified;

/// <summary>
/// 目前地圖的追緝倒數。從預警信件送出起顯示到最後一波；首波前 criticalAlertLeadTicks 轉紅。
/// RimWorld 自動實例化所有 Alert 葉子類別，不需註冊。
///
/// Countdown for the huntdown on the current map, shown from the warning letter to the last wave
/// and red within criticalAlertLeadTicks of the first wave. Vanilla instantiates every leaf Alert itself.
/// </summary>
public class Alert_FFF_Huntdown : Alert
{
    private HuntdownDef cachedDef;
    private HuntdownMapTimer cachedTimer;

    public Alert_FFF_Huntdown()
    {
        defaultPriority = AlertPriority.High;
    }

    private bool Red => cachedTimer != null
        && Find.TickManager.TicksGame > cachedTimer.raidTick - cachedDef.criticalAlertLeadTicks;

    private bool Critical => cachedTimer != null && Find.TickManager.TicksGame > cachedTimer.raidTick;

    protected override Color BGColor => Red ? Alert_Critical.BgColor() : Color.clear;

    public override AlertReport GetReport()
    {
        cachedDef = null;
        cachedTimer = null;
        Map map = Find.CurrentMap;
        if (map == null || !map.IsPlayerHome) return AlertReport.Inactive;

        // 同一張地圖被多個追緝時，顯示最快到的那個。With several huntdowns, show the soonest.
        int now = Find.TickManager.TicksGame;
        foreach (HuntdownInstance instance in HuntdownUtility.HuntersOf(map))
        {
            HuntdownMapTimer timer = instance.TimerFor(map);
            if (!timer.warned || timer.AllWavesFired(instance.def) || now > timer.LastWaveTick(instance.def)) continue;
            if (cachedTimer == null || timer.NextWaveTick(instance.def) < cachedTimer.NextWaveTick(cachedDef))
            {
                cachedDef = instance.def;
                cachedTimer = timer;
            }
        }
        return cachedTimer != null ? AlertReport.Active : AlertReport.Inactive;
    }

    public override string GetLabel()
    {
        if (cachedTimer == null) return string.Empty;
        string faction = HuntdownFactionName();
        if (Critical) return cachedDef.AlertLabelCritical.Formatted(faction);
        int ticks = cachedTimer.raidTick - Find.TickManager.TicksGame;
        return cachedDef.AlertLabel.Formatted(faction) + ": "
            + ticks.ToStringTicksToPeriod(allowSeconds: false, shortForm: false, canUseDecimals: false);
    }

    public override TaggedString GetExplanation()
    {
        if (cachedTimer == null) return TaggedString.Empty;
        string faction = HuntdownFactionName();
        return Critical
            ? cachedDef.AlertExplanationCritical.Formatted(faction)
            : cachedDef.AlertExplanation.Formatted(faction);
    }

    private string HuntdownFactionName()
    {
        return cachedDef.Worker.Faction?.Name ?? cachedDef.faction?.label ?? string.Empty;
    }
}
