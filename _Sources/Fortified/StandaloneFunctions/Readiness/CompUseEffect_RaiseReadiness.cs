using RimWorld;
using Verse;

namespace Fortified;

/// <summary>
/// 使用時（駭入終端、啟動召喚器……）有機率讓某個戒備升級，或累積壓力。
/// On use (hacking a terminal, activating a caller…) has a chance to raise a readiness, or adds pressure to it.
/// <code>
/// &lt;li Class="Fortified.CompProperties_UseEffect_RaiseReadiness"&gt;
///   &lt;readiness&gt;MyMod_Readiness&lt;/readiness&gt;
///   &lt;chance&gt;0.2&lt;/chance&gt;        &lt;!-- 擲中就升一級 a hit raises one level --&gt;
///   &lt;pressure&gt;0&lt;/pressure&gt;      &lt;!-- 或每次固定累積 or a fixed amount every use --&gt;
/// &lt;/li&gt;
/// </code>
/// </summary>
public class CompProperties_UseEffect_RaiseReadiness : CompProperties_UseEffect
{
    public ReadinessDef readiness;
    public float chance = 0.2f;
    public float pressure;

    public CompProperties_UseEffect_RaiseReadiness()
    {
        compClass = typeof(CompUseEffect_RaiseReadiness);
    }
}

public class CompUseEffect_RaiseReadiness : CompUseEffect
{
    public CompProperties_UseEffect_RaiseReadiness Props => (CompProperties_UseEffect_RaiseReadiness)props;

    public override void DoEffect(Pawn usedBy)
    {
        base.DoEffect(usedBy);
        ReadinessDef def = Props.readiness ?? ReadinessUtility.DefFor(parent.Faction);
        if (def == null) return;
        if (Props.pressure > 0f) ReadinessUtility.AddPressure(def, Props.pressure, parent.def.defName);
        if (Props.chance > 0f && Rand.Chance(Props.chance)) ReadinessUtility.Raise(def, 1, parent.def.defName);
    }
}
