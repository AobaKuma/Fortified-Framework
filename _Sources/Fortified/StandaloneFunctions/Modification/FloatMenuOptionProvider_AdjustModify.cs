using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using static System.Net.Mime.MediaTypeNames;
using Verse.AI;

namespace Fortified
{
    public class FloatMenuOptionProvider_AdjustModify : FloatMenuOptionProvider
    {
        protected override bool Drafted => false;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool MechanoidCanDo => true;

        public override bool TargetPawnValid(Pawn pawn, FloatMenuContext context)
        {
            return base.TargetPawnValid(pawn, context) && pawn.health != null;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
        {
            var comps = clickedPawn.health.hediffSet.GetHediffComps<HediffComp_Modification>();
            if (comps.EnumerableNullOrEmpty()) yield break;
            foreach (HediffComp_Modification item in comps.Cast<HediffComp_Modification>())
            {
                yield return RimWorld.FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption("FFF.Modification_Remove".Translate(item.parent.LabelCap), delegate
                {
                    JobDef jobDef = item.Props.applyJob ?? FFF_DefOf.FFF_ModificationRemove;
                    if (jobDef == null) return;
                    Job job = ModificationJobUtility.MakeRemoveJob(jobDef, clickedPawn, item.parent);
                    context.FirstSelectedPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    FleckMaker.Static(clickedPawn.DrawPos, clickedPawn.MapHeld, FleckDefOf.FeedbackEquip);
                }, MenuOptionPriority.High), context.FirstSelectedPawn, clickedPawn);
            }
        }
    }
}
