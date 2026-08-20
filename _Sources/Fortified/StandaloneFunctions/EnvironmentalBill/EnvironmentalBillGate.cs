using RimWorld;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Verse;

namespace Fortified;

/// <summary>
/// The single enforcement point for <see cref="ModExt_EnvironmentalBill"/>.
/// <para>
/// The restriction used to live inside <see cref="Bill_Production_Environmental.ShouldDoNow"/>, which
/// meant it only ever applied to bills that <see cref="Harmony_BillUtility"/> had managed to swap out at
/// creation time. <c>BillUtility.MakeNewBill</c> hands back <c>Bill_ProductionWithUft</c>,
/// <c>Bill_ProductionMech</c>, <c>Bill_ResurrectMech</c> or <c>Bill_Autonomous</c> depending on the recipe,
/// and none of those could be replaced by a <see cref="Bill_Production"/> subclass — so any recipe using an
/// unfinished thing, a gestation cycle or a forming timer silently ignored its environmental requirements.
/// </para>
/// <para>
/// The gate is therefore driven from a postfix on every <c>ShouldDoNow</c> implementation instead
/// (see <c>Harmony_Bill_ShouldDoNow</c>), and works off the <see cref="Bill"/> base class alone: any bill
/// type, vanilla or modded, is covered. Everything here is static and side-effect free apart from the
/// deliberate suspend-and-notify in <see cref="Suspend"/>.
/// </para>
/// </summary>
public static class EnvironmentalBillGate
{
    /// <summary>
    /// Per-bill scratch state. <c>ShouldDoNow</c> is hit by every work scan, by the bill UI every frame,
    /// and — because overrides chain up to their base — more than once per call, so both the linked-facility
    /// walk and the environment probes are memoised for the current game tick. Nothing here is saved.
    /// </summary>
    private class Cache
    {
        public int exemptionTick = -1;
        public EnvironmentExemptions exemptions;
        public int passedTick = -1;
    }

    // Weak keys: a deleted bill takes its cache entry with it, so this can never grow without bound.
    private static readonly ConditionalWeakTable<Bill, Cache> caches = new ConditionalWeakTable<Bill, Cache>();

    /// <summary>
    /// Current tick, or -1 when there is no running game (main menu, def load, tests).
    /// Deliberately not <c>Find.TickManager</c>: that is <c>Current.Game.tickManager</c> and throws
    /// outright when no game is loaded, which is exactly the case a null check here is meant to cover.
    /// </summary>
    private static int CurrentTick
    {
        get
        {
            Game game = Current.Game;
            TickManager ticks = game?.tickManager;
            return ticks?.TicksGame ?? -1;
        }
    }

    private static Cache CacheFor(Bill bill) => caches.GetValue(bill, _ => new Cache());

    /// <summary>The thing the bill is worked at. Null when it cannot be resolved.</summary>
    public static Thing BillGiverThing(Bill bill) => bill?.billStack?.billGiver as Thing;

    public static ModExt_EnvironmentalBill ExtensionFor(Bill bill) =>
        bill?.recipe?.GetModExtension<ModExt_EnvironmentalBill>();

    /// <summary>
    /// Exemptions granted to this bill by the facilities linked to its bill giver.
    /// Returns <see cref="EnvironmentExemptions.None"/> whenever the recipe forbids facility exemptions,
    /// so callers never have to special-case <c>allowFacilityExemption</c> themselves.
    /// </summary>
    public static EnvironmentExemptions ExemptionsFor(Bill bill)
    {
        if (bill == null)
        {
            return EnvironmentExemptions.None;
        }

        ModExt_EnvironmentalBill ext = ExtensionFor(bill);
        if (ext == null || !ext.allowFacilityExemption)
        {
            return EnvironmentExemptions.None;
        }

        int tick = CurrentTick;
        if (tick < 0)
        {
            // No tick to key a cache on; recompute rather than serve something stale.
            return EnvironmentExemptions.Gather(BillGiverThing(bill), bill.recipe);
        }

        Cache cache = CacheFor(bill);
        if (cache.exemptionTick != tick)
        {
            cache.exemptions = EnvironmentExemptions.Gather(BillGiverThing(bill), bill.recipe);
            cache.exemptionTick = tick;
        }
        return cache.exemptions;
    }

    /// <summary>
    /// Whether the bill's environmental requirements are met right now. Recipes without the extension,
    /// and extensions that declare no requirement at all, always pass. A failing check suspends the bill
    /// and tells the player why.
    /// </summary>
    public static bool CanDoNow(Bill bill)
    {
        if (bill == null)
        {
            return true;
        }

        ModExt_EnvironmentalBill ext = ExtensionFor(bill);
        if (ext == null || !ext.AnyRestriction)
        {
            return true;
        }

        // Nothing works bills outside of play, and suspending (or worse, sending a message) while a map
        // is still being initialised would be a side effect on half-built state. Leave the verdict alone.
        if (Current.ProgramState != ProgramState.Playing)
        {
            return true;
        }

        // Already suspended: either we suspended it a moment ago (an override that chains up to its base
        // reaches this method twice per call) or the player did. Either way, do not re-check and above all
        // do not send the message again.
        if (bill.suspended)
        {
            return false;
        }

        int tick = CurrentTick;
        Cache cache = tick < 0 ? null : CacheFor(bill);
        if (cache != null && cache.passedTick == tick)
        {
            return true;
        }

        Thing bench = BillGiverThing(bill);
        if (bench == null || bench.Destroyed || !bench.Spawned || bench.Map == null)
        {
            // No environment to evaluate. Do not suspend the bill over a transient state
            // (minified, being moved, mid-despawn) — just refuse to start work this scan.
            return false;
        }

        List<string> reasons = new List<string>();
        if (!EnvironmentOk(ext, ExemptionsFor(bill), bench, reasons))
        {
            Suspend(bill, bench, reasons);
            return false;
        }

        if (cache != null)
        {
            cache.passedTick = tick;
        }
        return true;
    }

    /// <summary>
    /// Runs every declared requirement that is not waived, collecting the player-facing reasons.
    /// Returns true only when nothing failed.
    /// </summary>
    private static bool EnvironmentOk(ModExt_EnvironmentalBill ext, EnvironmentExemptions ex, Thing bench,
                                      List<string> reasons)
    {
        bool ok = true;

        void Record(AcceptanceReport report)
        {
            if (report.Accepted)
            {
                return;
            }
            ok = false;
            string reason = report.Reason;
            if (!reason.NullOrEmpty() && !reasons.Contains(reason))
            {
                reasons.Add(reason);
            }
        }

        //潔净度相關
        if (ext.OnlyInCleanliness && !ex.cleanliness)
        {
            Record(EnvironmentUtility.InCleanRoom(bench, ex.EffectiveCleanliness(ext.CleanlinessRequirement)));
        }

        //溫度相關
        if (ext.TemperatureRestricted && !ex.temperature)
        {
            Record(EnvironmentUtility.InTemperature(bench, ex.EffectiveTemperatureRange(ext.AllowedTemperatureRange)));
        }

        //光照相關
        bool checkLightness = ext.LightnessRestricted && !ex.lightness;
        bool checkDarkness = ext.OnlyInDarkness && !ex.darkness;
        if (checkLightness && checkDarkness)
        {
            FloatRange band = new FloatRange(
                ex.EffectiveLightnessFloor(ext.LightnessRequirement),
                ex.EffectiveDarknessCeiling(ext.DarknessRequirement));
            Record(EnvironmentUtility.InLightnessBetween(bench, band));
        }
        else if (checkLightness)
        {
            Record(EnvironmentUtility.InLightness(bench, ex.EffectiveLightnessFloor(ext.LightnessRequirement)));
        }
        else if (checkDarkness)
        {
            Record(EnvironmentUtility.InDarkness(bench, ex.EffectiveDarknessCeiling(ext.DarknessRequirement)));
        }

        //真空相關
        bool checkPressure = ext.PressureRestricted && !ex.pressure;
        bool checkVacuum = ext.OnlyInVacuum && !ex.vacuum;
        if (checkPressure && checkVacuum)
        {
            FloatRange band = new FloatRange(
                ex.EffectiveVacuumFloor(ext.VacuumRequirement),
                ex.EffectivePressureCeiling(ext.PressureRequirement));
            Record(EnvironmentUtility.InPressureBetween(bench, band));
        }
        else if (checkPressure)
        {
            Record(EnvironmentUtility.InPressure(bench, ex.EffectivePressureCeiling(ext.PressureRequirement)));
        }
        else if (checkVacuum)
        {
            Record(EnvironmentUtility.InVacuum(bench, ex.EffectiveVacuumFloor(ext.VacuumRequirement)));
        }

        //重力相關
        if (ext.OnlyInMicroGravity && !ex.microGravity)
        {
            Record(EnvironmentUtility.InMicroGravity(bench));
        }

        return ok;
    }

    private static void Suspend(Bill bill, Thing bench, List<string> reasons)
    {
        bill.suspended = true;

        // An autonomous table that is already mid-run has to be told to stop; ShouldDoNow returning
        // false is not enough to interrupt work that has already started.
        if (bench is Building_WorkTableAutonomous autonomous && autonomous.IsWorking())
        {
            autonomous.Cancel();
        }

        string benchLabel = bench?.Label ?? bill.recipe?.LabelCap.ToString() ?? "?";
        string message = "FFF.Message.BillSuspended".Translate(bill.Label, benchLabel);
        if (reasons != null && reasons.Count > 0)
        {
            message = message + ": " + string.Join(", ", reasons);
        }
        Messages.Message(message, lookTargets: bench, MessageTypeDefOf.CautionInput);
    }
}
