using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Fortified
{
    public class Bill_Production_Environmental : Bill_Production
    {
        public Building_WorkTable WorkBench => billStack?.billGiver as Building_WorkTable;

        public ModExt_EnvironmentalBill Extension => recipe?.GetModExtension<ModExt_EnvironmentalBill>();

        /// <summary>
        /// Exemptions granted by linked facilities, recomputed at most once per game tick.
        /// <see cref="ShouldDoNow"/> is hit by every work scan and by the bill UI every frame,
        /// so the linked-facility walk is worth caching — but only within a single tick, so the
        /// result can never go stale across power flicks, refuels or relinks.
        /// </summary>
        public EnvironmentExemptions Exemptions
        {
            get
            {
                int tick = Find.TickManager?.TicksGame ?? -1;
                if (tick != cachedExemptionsTick || tick < 0)
                {
                    cachedExemptions = EnvironmentExemptions.Gather(WorkBench, recipe);
                    cachedExemptionsTick = tick;
                }
                return cachedExemptions;
            }
        }

        private EnvironmentExemptions cachedExemptions;
        private int cachedExemptionsTick = -1;

        public Bill_Production_Environmental()
        {
        }

        public Bill_Production_Environmental(RecipeDef recipe, Precept_ThingStyle precept = null) : base(recipe, precept)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
        }

        public override bool ShouldDoNow()
        {
            if (suspended || !base.ShouldDoNow())
            {
                return false;
            }
            if (!EnvironmentCanDoNow())
            {
                if (billStack?.billGiver is Building_WorkTableAutonomous at && at.IsWorking())
                {
                    at.Cancel();
                }
                return false;
            }
            return true;
        }

        protected bool EnvironmentCanDoNow()
        {
            ModExt_EnvironmentalBill ext = Extension;
            if (ext == null)
            {
                return true;
            }

            Building_WorkTable bench = WorkBench;
            if (bench == null || !bench.Spawned || bench.Map == null)
            {
                // No environment to evaluate. Do not suspend the bill over a transient state
                // (minified, being moved, mid-despawn) — just refuse to start work this scan.
                return false;
            }

            EnvironmentExemptions ex = ext.allowFacilityExemption ? Exemptions : EnvironmentExemptions.None;
            bool passed = true;

            //潔净度相關
            if (ext.OnlyInCleanliness && !ex.cleanliness)
            {
                AcceptanceReport report = EnvironmentUtility.InCleanRoom(bench, ex.EffectiveCleanliness(ext.CleanlinessRequirement));
                if (!report.Accepted) passed = SendSuspendedMessage(bench, report.Reason);
            }

            //溫度相關
            if (ext.TemperatureRestricted && !ex.temperature)
            {
                AcceptanceReport report = EnvironmentUtility.InTemperature(bench, ex.EffectiveTemperatureRange(ext.AllowedTemperatureRange));
                if (!report.Accepted) passed = SendSuspendedMessage(bench, report.Reason);
            }

            //光照相關
            bool checkLightness = ext.LightnessRestricted && !ex.lightness;
            bool checkDarkness = ext.OnlyInDarkness && !ex.darkness;
            if (checkLightness && checkDarkness)
            {
                FloatRange band = new FloatRange(
                    ex.EffectiveLightnessFloor(ext.LightnessRequirement),
                    ex.EffectiveDarknessCeiling(ext.DarknessRequirement));
                AcceptanceReport report = EnvironmentUtility.InLightnessBetween(bench, band);
                if (!report.Accepted) passed = SendSuspendedMessage(bench, report.Reason);
            }
            else if (checkLightness)
            {
                AcceptanceReport report = EnvironmentUtility.InLightness(bench, ex.EffectiveLightnessFloor(ext.LightnessRequirement));
                if (!report.Accepted) passed = SendSuspendedMessage(bench, report.Reason);
            }
            else if (checkDarkness)
            {
                AcceptanceReport report = EnvironmentUtility.InDarkness(bench, ex.EffectiveDarknessCeiling(ext.DarknessRequirement));
                if (!report.Accepted) passed = SendSuspendedMessage(bench, report.Reason);
            }

            //真空相關
            bool checkPressure = ext.PressureRestricted && !ex.pressure;
            bool checkVacuum = ext.OnlyInVacuum && !ex.vacuum;
            if (checkPressure && checkVacuum)
            {
                FloatRange band = new FloatRange(
                    ex.EffectiveVacuumFloor(ext.VacuumRequirement),
                    ex.EffectivePressureCeiling(ext.PressureRequirement));
                AcceptanceReport report = EnvironmentUtility.InPressureBetween(bench, band);
                if (!report.Accepted) passed = SendSuspendedMessage(bench, report.Reason);
            }
            else if (checkPressure)
            {
                AcceptanceReport report = EnvironmentUtility.InPressure(bench, ex.EffectivePressureCeiling(ext.PressureRequirement));
                if (!report.Accepted) passed = SendSuspendedMessage(bench, report.Reason);
            }
            else if (checkVacuum)
            {
                AcceptanceReport report = EnvironmentUtility.InVacuum(bench, ex.EffectiveVacuumFloor(ext.VacuumRequirement));
                if (!report.Accepted) passed = SendSuspendedMessage(bench, report.Reason);
            }

            //重力相關
            if (ext.OnlyInMicroGravity && !ex.microGravity)
            {
                AcceptanceReport report = EnvironmentUtility.InMicroGravity(bench);
                if (!report.Accepted) passed = SendSuspendedMessage(bench, report.Reason);
            }

            return passed;
        }

        private bool SendSuspendedMessage(Thing bench, string reason)
        {
            if (!suspended) suspended = true;

            string benchLabel = bench?.Label ?? recipe?.LabelCap.ToString() ?? "?";
            string message = "FFF.Message.BillSuspended".Translate(Label, benchLabel);
            if (!reason.NullOrEmpty())
            {
                message = message + ": " + reason;
            }
            Messages.Message(message, lookTargets: bench, MessageTypeDefOf.CautionInput);
            return false;
        }

        protected override string StatusString
        {
            get
            {
                string baseStatus = base.StatusString;
                if (!baseStatus.NullOrEmpty())
                {
                    return baseStatus;
                }

                ModExt_EnvironmentalBill ext = Extension;
                if (ext == null || !ext.allowFacilityExemption)
                {
                    return "";
                }

                EnvironmentExemptions ex = Exemptions;
                if (!ex.Any)
                {
                    return "";
                }
                string line = "FFF.EnvironmentExemption.StatusLine".Translate(ex.Describe());
                return " " + line;
            }
        }

        protected override float StatusLineMinHeight
        {
            get
            {
                float baseHeight = base.StatusLineMinHeight;
                if (baseHeight > 0f)
                {
                    return baseHeight;
                }
                return StatusString.NullOrEmpty() ? 0f : 24f;
            }
        }

        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            if (repeatMode == BillRepeatModeDefOf.RepeatCount)
            {
                if (repeatCount > 0)
                {
                    repeatCount--;
                }
                if (repeatCount == 0)
                {
                    Messages.Message("MessageBillComplete".Translate(LabelCap), (Thing)billStack.billGiver, MessageTypeDefOf.TaskCompletion);
                }
            }
            recipe.Worker.Notify_IterationCompleted(billDoer, ingredients);
        }
    }
}
