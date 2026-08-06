using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Fortified;

public class ModExt_EnvironmentalBill : DefModExtension
{
    //之後應該會改成XXXRestricted的命名。

    public bool OnlyInCleanliness = false;
    public float CleanlinessRequirement = 0.1f;

    public bool OnlyInDarkness = false; //光照上限
    public float DarknessRequirement = 0.25f;

    public bool LightnessRestricted = false; //光照下限
    public float LightnessRequirement = 0.75f;

    public bool PressureRestricted = false;
    public float PressureRequirement = 0.75f; //真空上限

    public bool OnlyInVacuum = false;
    public float VacuumRequirement = 0.5f; //真空下限

    public bool TemperatureRestricted = false;
    public FloatRange AllowedTemperatureRange = FloatRange.Zero;

    public bool OnlyInMicroGravity = false;

    /// <summary>
    /// When true (the default) a linked facility carrying <see cref="CompEnvironmentExemption"/>
    /// can waive or relax these requirements. Set false to make the recipe unconditionally strict.
    /// </summary>
    public bool allowFacilityExemption = true;

    /// <summary>True when at least one environmental requirement is actually declared.</summary>
    public bool AnyRestriction =>
        OnlyInCleanliness || OnlyInDarkness || LightnessRestricted ||
        PressureRestricted || OnlyInVacuum || TemperatureRestricted || OnlyInMicroGravity;

    public IEnumerable<StatDrawEntry> SpecialDisplayStats()
    {
        List<string> reportTexts = new();
        if (OnlyInCleanliness)
            reportTexts.Add("FFF.CleanRoom".Translate(CleanlinessRequirement.ToString("0.##")));

        if (TemperatureRestricted)
            reportTexts.Add("FFF.Temperature".Translate(
                AllowedTemperatureRange.min.ToStringTemperature("F0"),
                AllowedTemperatureRange.max.ToStringTemperature("F0")));

        if (LightnessRestricted && OnlyInDarkness)
        {
            //明亮是100%。
            reportTexts.Add("FFF.LightnessDarkness".Translate(DarknessRequirement.ToStringPercent(), LightnessRequirement.ToStringPercent()));
        }
        else
        {
            if (LightnessRestricted) reportTexts.Add("FFF.Lightness".Translate(LightnessRequirement.ToStringPercent()));
            if (OnlyInDarkness) reportTexts.Add("FFF.Darkness".Translate(DarknessRequirement.ToStringPercent()));
        }

        if (PressureRestricted && OnlyInVacuum)
        {
            //真空是100%。
            reportTexts.Add("FFF.PressureVacuum".Translate(PressureRequirement.ToStringPercent(), VacuumRequirement.ToStringPercent()));
        }
        else
        {
            if (PressureRestricted) reportTexts.Add("FFF.PressureRestriction".Translate(PressureRequirement.ToStringPercent()));
            if (OnlyInVacuum) reportTexts.Add("FFF.Vacuum".Translate(VacuumRequirement.ToStringPercent()));
        }

        if (OnlyInMicroGravity) reportTexts.Add("FFF.MicroGravity".Translate());

        if (allowFacilityExemption && AnyRestriction)
            reportTexts.Add("FFF.EnvironmentExemption.RecipeNote".Translate());

        yield return new StatDrawEntry(
            StatCategoryDefOf.Basics,
            "FFF.EnvironmentRestriction".Translate(),
            string.Join("\n", reportTexts),
            null, 1145);
    }
    public override IEnumerable<string> ConfigErrors()
    {
        // When both bounds are declared they form a band; the checks normalise the ordering,
        // so only a zero-width (unsatisfiable in practice) band is a real authoring mistake.
        if (OnlyInDarkness && LightnessRestricted && DarknessRequirement == LightnessRequirement)
        {
            yield return "DarknessRequirement equals LightnessRequirement, producing a zero-width light band.";
        }
        if (OnlyInVacuum && PressureRestricted && VacuumRequirement == PressureRequirement)
        {
            yield return "VacuumRequirement equals PressureRequirement, producing a zero-width vacuum band.";
        }
        if (TemperatureRestricted && AllowedTemperatureRange.min >= AllowedTemperatureRange.max)
        {
            yield return "AllowedTemperatureRange min is higher than or equal to max.";
        }
        if (!ModsConfig.OdysseyActive && (OnlyInMicroGravity || OnlyInVacuum))
        {
            yield return $"Error on {ToString()} an environmental bill requires Odyssey to be active to function properly.";
        }
        foreach (var error in base.ConfigErrors())
        {
            yield return error;
        }
    }
}