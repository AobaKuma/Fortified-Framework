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
    //
    // These are declared as raw defNames on purpose. DO NOT change them back to
    // List<RecipeDef> / List<ThingDef>.
    //
    // RimWorld's cross-reference loader (DirectXmlCrossRefLoader.WantedRefForList.TryResolve)
    // builds a def-typed list by *adding only the entries it managed to resolve*. An <li> that
    // names a def which does not exist — a typo, a renamed def, or a def belonging to a mod that
    // is not active — is never added; it does not even leave a null behind. An entry carrying
    // MayRequire for an inactive mod is dropped without so much as a log line.
    //
    // With def-typed lists, a whitelist whose entries all fail to resolve silently collapses to an
    // empty list, which NullOrEmpty() then reads as "no restriction" — so a facility that was meant
    // to help exactly one bench (or one recipe) quietly turns into a blanket waiver for every
    // environmental requirement in the game. Whitelists must fail closed, not open.
    //
    // Keeping the raw names means a missing target simply never matches anything: the whitelist
    // stays non-empty, the facility keeps helping nothing, and ConfigErrors reports the dangling
    // entry so the author can see it. The XML syntax is unchanged (<li>SomeDefName</li>).
    public List<string> onlyForRecipes;
    public List<string> onlyForWorkTables;

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

    /// <summary>
    /// Whitelist test. An undeclared (null / empty) list means "no restriction"; a declared list
    /// only ever passes on an exact defName match, so an entry naming something that does not
    /// exist can never widen the whitelist.
    /// </summary>
    private static bool NameAllowed(List<string> whitelist, string defName)
    {
        if (whitelist.NullOrEmpty())
        {
            return true;
        }
        if (defName.NullOrEmpty())
        {
            return false;
        }
        for (int i = 0; i < whitelist.Count; i++)
        {
            if (whitelist[i] == defName)
            {
                return true;
            }
        }
        return false;
    }

    public bool AllowsRecipe(RecipeDef recipe) => NameAllowed(onlyForRecipes, recipe?.defName);

    public bool AllowsWorkTable(Thing workTable) => NameAllowed(onlyForWorkTables, workTable?.def?.defName);

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

        // Dangling whitelist entries are not fatal — an unmatched name just means this facility
        // helps nothing, which is the safe direction — but they are almost always a typo or a
        // missing dependency, so say so out loud.
        foreach (string error in WhitelistErrors(parentDef, onlyForRecipes, "onlyForRecipes",
                                                 name => DefDatabase<RecipeDef>.GetNamedSilentFail(name) != null))
        {
            yield return error;
        }

        foreach (string error in WhitelistErrors(parentDef, onlyForWorkTables, "onlyForWorkTables",
                                                 name => DefDatabase<ThingDef>.GetNamedSilentFail(name) != null))
        {
            yield return error;
        }
    }

    private static IEnumerable<string> WhitelistErrors(ThingDef parentDef, List<string> whitelist,
                                                       string fieldName, System.Func<string, bool> exists)
    {
        if (whitelist.NullOrEmpty())
        {
            yield break;
        }

        bool blank = false;
        List<string> unresolved = null;

        for (int i = 0; i < whitelist.Count; i++)
        {
            string name = whitelist[i];
            if (name.NullOrEmpty())
            {
                blank = true;
                continue;
            }
            if (!exists(name))
            {
                (unresolved ??= new List<string>()).Add(name);
            }
        }

        if (blank)
        {
            yield return $"{parentDef.defName} CompEnvironmentExemption.{fieldName} contains a blank entry.";
        }

        if (unresolved != null)
        {
            yield return $"{parentDef.defName} CompEnvironmentExemption.{fieldName} names no loaded def: " +
                         string.Join(", ", unresolved) +
                         ". Those entries can never be matched, so the facility grants nothing for them. " +
                         "Check for a typo, or that the mod providing them is active.";
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
        if (!p.AllowsRecipe(recipe))
        {
            return false;
        }
        if (!p.AllowsWorkTable(workTable))
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
