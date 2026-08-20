using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Fortified
{
    /// <summary>
    /// A production bill that shows the environmental requirements of its recipe in the bill UI.
    /// <para>
    /// The requirements themselves are NOT enforced here — <see cref="EnvironmentalBillGate"/> is hooked
    /// into every <c>ShouldDoNow</c> implementation instead, so recipes that end up as some other bill
    /// type (unfinished-thing, mech gestation, forming) are covered too. The override below is kept as a
    /// direct, patch-independent call into the same gate: if the Harmony patch ever fails to apply, bills
    /// of this type still refuse to run in the wrong environment.
    /// </para>
    /// </summary>
    public class Bill_Production_Environmental : Bill_Production
    {
        public Building_WorkTable WorkBench => billStack?.billGiver as Building_WorkTable;

        public ModExt_EnvironmentalBill Extension => EnvironmentalBillGate.ExtensionFor(this);

        /// <summary>
        /// Exemptions granted by linked facilities. Cached per game tick by the gate — <see cref="ShouldDoNow"/>
        /// is hit by every work scan and by the bill UI every frame, so the linked-facility walk is worth
        /// caching, but only within a single tick so the result can never go stale across power flicks,
        /// refuels or relinks.
        /// </summary>
        public EnvironmentExemptions Exemptions => EnvironmentalBillGate.ExemptionsFor(this);

        public Bill_Production_Environmental()
        {
        }

        public Bill_Production_Environmental(RecipeDef recipe, Precept_ThingStyle precept = null) : base(recipe, precept)
        {
        }

        public override bool ShouldDoNow()
        {
            if (suspended || !base.ShouldDoNow())
            {
                return false;
            }
            return EnvironmentalBillGate.CanDoNow(this);
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
