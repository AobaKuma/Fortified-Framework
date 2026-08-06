using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Fortified;

/// <summary>
/// Properties for <see cref="CompEnvironmentExemption"/>.
/// <para>
/// Attach to a <b>facility</b> building — the parent def must also carry a
/// <see cref="CompProperties_Facility"/>, otherwise nothing can ever link to it.
/// While the facility is linked to a work table and considered active, it waives
/// (or merely relaxes) the environmental requirements declared by
/// <see cref="ModExt_EnvironmentalBill"/> on the recipe being crafted.
/// </para>
/// </summary>
public class CompProperties_EnvironmentExemption : CompProperties
{
    // ---- Full waivers: the corresponding check is skipped entirely. ----
    public bool exemptCleanliness = false;
    public bool exemptTemperature = false;
    public bool exemptLightness = false;
    public bool exemptDarkness = false;
    public bool exemptPressure = false;
    public bool exemptVacuum = false;
    public bool exemptMicroGravity = false;

    // ---- Partial relaxation: "how much slack this facility adds". ----
    // Always expressed as a non-negative magnitude; the direction is implied by the
    // requirement it loosens. Values from multiple linked facilities are summed.
    public float cleanlinessOffset = 0f;          // lowers the required cleanliness floor
    public float temperatureRangeExpansion = 0f;  // widens the allowed range on both ends (Celsius degrees)
    public float lightnessOffset = 0f;            // lowers the required light floor
    public float darknessOffset = 0f;             // raises the allowed light ceiling
    public float pressureOffset = 0f;             // raises the allowed vacuum ceiling
    public float vacuumOffset = 0f;               // lowers the required vacuum floor

    // ---- Gating. Each only applies when the parent actually has the relevant comp. ----
    public bool requiresFuel = true;          // CompRefuelable.HasFuel
    public bool requiresSwitchedOn = true;    // CompFlickable.SwitchIsOn
    public bool requiresNotBroken = true;     // !CompBreakdownable.BrokenDown

    // ---- Optional whitelists. null / empty means "no restriction". ----
    public List<RecipeDef> onlyForRecipes;
    public List<ThingDef> onlyForWorkTables;

    public CompProperties_EnvironmentExemption()
    {
        compClass = typeof(CompEnvironmentExemption);
    }

    /// <summary>True when this comp would actually change any environmental check.</summary>
    public bool GrantsAnything =>
        exemptCleanliness || exemptTemperature || exemptLightness || exemptDarkness ||
        exemptPressure || exemptVacuum || exemptMicroGravity ||
        cleanlinessOffset > 0f || temperatureRangeExpansion > 0f ||
        lightnessOffset > 0f || darknessOffset > 0f ||
        pressureOffset > 0f || vacuumOffset > 0f;

    public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
    {
        foreach (string error in base.ConfigErrors(parentDef))
        {
            yield return error;
        }

        if (parentDef == null)
        {
            yield break;
        }

        if (parentDef.GetCompProperties<CompProperties_Facility>() == null)
        {
            yield return $"{parentDef.defName} has CompEnvironmentExemption but no CompFacility; " +
                         "no work table will ever be able to link to it.";
        }

        if (!GrantsAnything)
        {
            yield return $"{parentDef.defName} has CompEnvironmentExemption but grants no exemption " +
                         "and no offset; it will do nothing.";
        }

        if (cleanlinessOffset < 0f || temperatureRangeExpansion < 0f || lightnessOffset < 0f ||
            darknessOffset < 0f || pressureOffset < 0f || vacuumOffset < 0f)
        {
            yield return $"{parentDef.defName} CompEnvironmentExemption has a negative offset. " +
                         "Offsets are magnitudes of added slack and must be >= 0; negative values are clamped to 0.";
        }

        if (!ModsConfig.OdysseyActive && (exemptMicroGravity || exemptVacuum || exemptPressure ||
                                          vacuumOffset > 0f || pressureOffset > 0f))
        {
            yield return $"{parentDef.defName} CompEnvironmentExemption declares vacuum/gravity exemptions, " +
                         "which only matter when Odyssey is active.";
        }

        if (!onlyForRecipes.NullOrEmpty())
        {
            for (int i = 0; i < onlyForRecipes.Count; i++)
            {
                if (onlyForRecipes[i] == null)
                {
                    yield return $"{parentDef.defName} CompEnvironmentExemption.onlyForRecipes contains a null entry.";
                    break;
                }
            }
        }

        if (!onlyForWorkTables.NullOrEmpty())
        {
            for (int i = 0; i < onlyForWorkTables.Count; i++)
            {
                if (onlyForWorkTables[i] == null)
                {
                    yield return $"{parentDef.defName} CompEnvironmentExemption.onlyForWorkTables contains a null entry.";
                    break;
                }
            }
        }
    }
}

/// <summary>
/// Facility-side comp that supplies environmental exemptions to linked work tables.
/// Purely a data provider — it never ticks and holds no saved state.
/// </summary>
public class CompEnvironmentExemption : ThingComp
{
    public CompProperties_EnvironmentExemption Props => props as CompProperties_EnvironmentExemption;

    private bool cachedComps;
    private CompRefuelable refuelable;
    private CompFlickable flickable;
    private CompBreakdownable breakdownable;

    private void EnsureComps()
    {
        // Sibling comps are not guaranteed to exist during Initialize, so resolve lazily.
        if (cachedComps || parent == null)
        {
            return;
        }
        refuelable = parent.TryGetComp<CompRefuelable>();
        flickable = parent.TryGetComp<CompFlickable>();
        breakdownable = parent.TryGetComp<CompBreakdownable>();
        cachedComps = true;
    }

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        EnsureComps();
    }

    /// <summary>
    /// Local operability: fuel / switch / breakdown. Power and link validity are handled by
    /// <see cref="CompFacility.CanBeActive"/> and checked separately by the consumer.
    /// </summary>
    public bool Operational
    {
        get
        {
            CompProperties_EnvironmentExemption p = Props;
            if (p == null || parent == null || parent.Destroyed)
            {
                return false;
            }
            EnsureComps();
            if (p.requiresFuel && refuelable != null && !refuelable.HasFuel)
            {
                return false;
            }
            if (p.requiresSwitchedOn && flickable != null && !flickable.SwitchIsOn)
            {
                return false;
            }
            if (p.requiresNotBroken && breakdownable != null && breakdownable.BrokenDown)
            {
                return false;
            }
            return true;
        }
    }

    /// <summary>Whether this facility grants its exemptions for the given work table and recipe.</summary>
    public bool AppliesTo(Thing workTable, RecipeDef recipe)
    {
        CompProperties_EnvironmentExemption p = Props;
        if (p == null || !p.GrantsAnything)
        {
            return false;
        }
        if (!p.onlyForRecipes.NullOrEmpty() && (recipe == null || !p.onlyForRecipes.Contains(recipe)))
        {
            return false;
        }
        if (!p.onlyForWorkTables.NullOrEmpty() && (workTable == null || !p.onlyForWorkTables.Contains(workTable.def)))
        {
            return false;
        }
        return Operational;
    }

    public override string CompInspectStringExtra()
    {
        CompProperties_EnvironmentExemption p = Props;
        if (p == null || !p.GrantsAnything)
        {
            return null;
        }

        // CompFacility owns power/link state; if it is missing the def is misconfigured (ConfigErrors reports it).
        CompFacility facility = parent == null ? null : parent.GetComp<CompFacility>();
        bool active = facility != null && facility.CanBeActive && Operational;

        string label = "FFF.EnvironmentExemption.Label".Translate();
        string inactive = "FFF.EnvironmentExemption.Inactive".Translate();
        string value = active ? EnvironmentExemptions.FromProps(p).Describe() : inactive;
        return label + ": " + value;
    }

    public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
    {
        foreach (StatDrawEntry entry in base.SpecialDisplayStats())
        {
            yield return entry;
        }

        CompProperties_EnvironmentExemption p = Props;
        if (p == null || !p.GrantsAnything)
        {
            yield break;
        }

        yield return new StatDrawEntry(
            StatCategoryDefOf.Building,
            "FFF.EnvironmentExemption.Label".Translate(),
            EnvironmentExemptions.FromProps(p).Describe(),
            "FFF.EnvironmentExemption.Desc".Translate(),
            1144);
    }
}
