using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Fortified;

/// <summary>
/// The combined environmental slack granted to one work table by every active linked
/// facility carrying a <see cref="CompEnvironmentExemption"/>.
/// <para>
/// Booleans OR together; offsets sum. The struct is a value type and
/// <c>default</c> means "no exemptions at all", so a failed gather is always safe.
/// </para>
/// </summary>
public struct EnvironmentExemptions
{
    public bool cleanliness;
    public bool temperature;
    public bool lightness;
    public bool darkness;
    public bool pressure;
    public bool vacuum;
    public bool microGravity;

    public float cleanlinessOffset;
    public float temperatureRangeExpansion;
    public float lightnessOffset;
    public float darknessOffset;
    public float pressureOffset;
    public float vacuumOffset;

    public static readonly EnvironmentExemptions None = default;

    public bool Any =>
        cleanliness || temperature || lightness || darkness || pressure || vacuum || microGravity ||
        cleanlinessOffset > 0f || temperatureRangeExpansion > 0f ||
        lightnessOffset > 0f || darknessOffset > 0f ||
        pressureOffset > 0f || vacuumOffset > 0f;

    /// <summary>Projection of a single comp's props, with negative offsets clamped away.</summary>
    public static EnvironmentExemptions FromProps(CompProperties_EnvironmentExemption p)
    {
        EnvironmentExemptions result = default;
        if (p == null)
        {
            return result;
        }
        result.Absorb(p);
        return result;
    }

    public void Absorb(CompProperties_EnvironmentExemption p)
    {
        if (p == null)
        {
            return;
        }
        cleanliness |= p.exemptCleanliness;
        temperature |= p.exemptTemperature;
        lightness |= p.exemptLightness;
        darkness |= p.exemptDarkness;
        pressure |= p.exemptPressure;
        vacuum |= p.exemptVacuum;
        microGravity |= p.exemptMicroGravity;

        // Offsets are magnitudes of added slack; a negative value would silently tighten
        // the requirement, which is never the intent. Clamp instead.
        cleanlinessOffset += Mathf.Max(0f, p.cleanlinessOffset);
        temperatureRangeExpansion += Mathf.Max(0f, p.temperatureRangeExpansion);
        lightnessOffset += Mathf.Max(0f, p.lightnessOffset);
        darknessOffset += Mathf.Max(0f, p.darknessOffset);
        pressureOffset += Mathf.Max(0f, p.pressureOffset);
        vacuumOffset += Mathf.Max(0f, p.vacuumOffset);
    }

    /// <summary>
    /// Collect exemptions from the facilities currently linked to <paramref name="workTable"/>.
    /// Never throws and never returns partial garbage — on any unexpected state it returns <see cref="None"/>.
    /// </summary>
    public static EnvironmentExemptions Gather(Thing workTable, RecipeDef recipe)
    {
        EnvironmentExemptions result = default;
        if (workTable == null || !workTable.Spawned || workTable.Destroyed)
        {
            return result;
        }

        CompAffectedByFacilities affected = workTable.TryGetComp<CompAffectedByFacilities>();
        if (affected == null)
        {
            return result;
        }

        List<Thing> linked = affected.LinkedFacilitiesListForReading;
        if (linked.NullOrEmpty())
        {
            return result;
        }

        for (int i = 0; i < linked.Count; i++)
        {
            Thing facility = linked[i];
            if (facility == null || facility.Destroyed || !facility.Spawned)
            {
                continue;
            }

            CompEnvironmentExemption exemption = facility.TryGetComp<CompEnvironmentExemption>();
            if (exemption == null)
            {
                continue;
            }

            // Deliberately not CompAffectedByFacilities.IsFacilityActive: that dereferences
            // CompFacility unconditionally and would NRE on a malformed def.
            CompFacility comp = facility.TryGetComp<CompFacility>();
            if (comp == null || !comp.CanBeActive)
            {
                continue;
            }

            if (!exemption.AppliesTo(workTable, recipe))
            {
                continue;
            }

            result.Absorb(exemption.Props);
        }

        return result;
    }

    // ---- Effective-requirement helpers. Each returns the requirement after slack is applied. ----

    public float EffectiveCleanliness(float required) => required - cleanlinessOffset;

    public FloatRange EffectiveTemperatureRange(FloatRange required) =>
        temperatureRangeExpansion > 0f ? required.ExpandedBy(temperatureRangeExpansion) : required;

    /// <summary>Light floor, clamped to [0, 1].</summary>
    public float EffectiveLightnessFloor(float required) => Mathf.Clamp01(required - lightnessOffset);

    /// <summary>Light ceiling, clamped to [0, 1].</summary>
    public float EffectiveDarknessCeiling(float required) => Mathf.Clamp01(required + darknessOffset);

    /// <summary>Vacuum ceiling (pressurized requirement), clamped to [0, 1].</summary>
    public float EffectivePressureCeiling(float required) => Mathf.Clamp01(required + pressureOffset);

    /// <summary>Vacuum floor, clamped to [0, 1].</summary>
    public float EffectiveVacuumFloor(float required) => Mathf.Clamp01(required - vacuumOffset);

    /// <summary>Human-readable one-line summary. Returns the "none" string when empty.</summary>
    public string Describe()
    {
        List<string> parts = new List<string>();

        if (cleanliness) parts.Add("FFF.EnvironmentExemption.Cleanliness".Translate());
        else if (cleanlinessOffset > 0f) parts.Add("FFF.EnvironmentExemption.CleanlinessOffset".Translate(cleanlinessOffset.ToString("0.##")));

        if (temperature) parts.Add("FFF.EnvironmentExemption.Temperature".Translate());
        else if (temperatureRangeExpansion > 0f) parts.Add("FFF.EnvironmentExemption.TemperatureOffset".Translate(temperatureRangeExpansion.ToStringTemperatureOffset("F0")));

        if (lightness) parts.Add("FFF.EnvironmentExemption.Lightness".Translate());
        else if (lightnessOffset > 0f) parts.Add("FFF.EnvironmentExemption.LightnessOffset".Translate(lightnessOffset.ToStringPercent()));

        if (darkness) parts.Add("FFF.EnvironmentExemption.Darkness".Translate());
        else if (darknessOffset > 0f) parts.Add("FFF.EnvironmentExemption.DarknessOffset".Translate(darknessOffset.ToStringPercent()));

        if (pressure) parts.Add("FFF.EnvironmentExemption.Pressure".Translate());
        else if (pressureOffset > 0f) parts.Add("FFF.EnvironmentExemption.PressureOffset".Translate(pressureOffset.ToStringPercent()));

        if (vacuum) parts.Add("FFF.EnvironmentExemption.Vacuum".Translate());
        else if (vacuumOffset > 0f) parts.Add("FFF.EnvironmentExemption.VacuumOffset".Translate(vacuumOffset.ToStringPercent()));

        if (microGravity) parts.Add("FFF.EnvironmentExemption.MicroGravity".Translate());

        return parts.Count == 0
            ? "FFF.EnvironmentExemption.NoneLabel".Translate().ToString()
            : string.Join(", ", parts);
    }
}
