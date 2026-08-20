using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Fortified
{
    public class CompTargetable_AddHediffOnTarget : CompTargetable
    {
        public new CompProperties_AddHediffOnTarget Props => (CompProperties_AddHediffOnTarget)props;
        protected override TargetingParameters GetTargetingParameters()
        {
            return new TargetingParameters
            {
                canTargetPawns = true,
                canTargetHumans = false,
                canTargetSubhumans = false,
                canTargetAnimals = false,
                canTargetMechs = true,
                canTargetBuildings = false,
                canTargetLocations = false
            };
        }
        protected override bool PlayerChoosesTarget => true;
        public override IEnumerable<Thing> GetTargets(Thing targetChosenByPlayer = null)
        {
            yield return targetChosenByPlayer;
        }
        public override void DoEffect(Pawn usedBy)
        {

            if (this.PlayerChoosesTarget && this.selectedTarget == null)
            {
                return;
            }
            if (this.selectedTarget != null && !this.GetTargetingParameters().CanTarget(this.selectedTarget, null))
            {
                return;
            }
            if (!ModificationUtility.SupportedByRace((Pawn)selectedTarget, Props))
            {
                Messages.Message("FFF.Modification_RaceNotSupported".Translate(), MessageTypeDefOf.NeutralEvent);
                return;
            }
            Pawn targetPawn = (Pawn)selectedTarget;
            // This path is invoked from the item's active use job. The selected stack
            // is already reserved by usedBy, so a map-wide CanReserve check would
            // incorrectly hide that stack (and make a second stack appear required).
            // The apply job validates and resolves the actual material again when it starts.
            if (!ModificationInstallValidator.TryFindInstallPart(targetPawn, parent.def, null, out BodyPartRecord part, out string reason, false))
            {
                Messages.Message(reason ?? "FFF.Modification_NoValidPart".Translate(), MessageTypeDefOf.NeutralEvent);
                return;
            }
            if (usedBy.IsColonistPlayerControlled)
            {
                Job job = ModificationJobUtility.MakeApplyJob(FFF_DefOf.FFF_Modification, targetPawn, parent, part);
                usedBy.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }
        }
    }
    public class CompProperties_AddHediffOnTarget : CompProperties_Targetable
    {
        public HediffDef hediffDef;
        public SoundDef soundDef;
        public List<BodyPartDef> targetBodyPartDefs = null;//如果為Null，那就是默認給全身，如果有值，那就是查找目標是否有這個部位。
        public List<ThingDef>supportRaceDefs = null;//如果為null，那就是所有機械體都能裝。這個限制目前是給戰鬥框架的改造用的。
    }
}
