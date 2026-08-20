using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Fortified
{
    public class JobDriver_RemoveModification : JobDriver
    {
        private const int DurationTicks = 600;

        private Pawn TargetPawn => (Pawn)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (TargetPawn == null) return false;
            return TargetPawn == pawn || pawn.Reserve(TargetPawn, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            if (TargetPawn != pawn)
            {
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            }

            Toil wait = Toils_General.WaitWith(TargetIndex.A, DurationTicks, true, true);
            wait.FailOnDespawnedOrNull(TargetIndex.A);
            wait.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            wait.WithEffect(EffecterDefOf.MechRepairing, TargetIndex.A);
            wait.handlingFacing = true;
            yield return wait;
            yield return Toils_General.Do(RemoveModification);
        }

        private void RemoveModification()
        {
            HediffComp_Modification mod = FindTargetModification();
            if (mod == null)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            Hediff hediff = mod.parent;
            Messages.Message("FFF.Message.Modification.Removed".Translate(TargetPawn), TargetPawn, MessageTypeDefOf.PositiveEvent);
            mod.isApplyTarget = false;
            TargetPawn.health.RemoveHediff(hediff);

            if (hediff.def.spawnThingOnRemoved != null && TargetPawn.MapHeld != null)
            {
                Thing thing = ThingMaker.MakeThing(hediff.def.spawnThingOnRemoved);
                thing.stackCount = 1;
                GenPlace.TryPlaceThing(thing, TargetPawn.Position, TargetPawn.MapHeld, ThingPlaceMode.Near);
            }
            EndJobWith(JobCondition.Succeeded);
        }

        private HediffComp_Modification FindTargetModification()
        {
            if (TargetPawn?.health?.hediffSet == null) return null;
            Job_Modification preciseJob = job as Job_Modification;
            BodyPartRecord targetPart = preciseJob?.ResolvePart(TargetPawn);
            if (preciseJob != null && targetPart == null && (preciseJob.targetPartIndex >= 0 || !preciseJob.targetPartDefName.NullOrEmpty())) return null;
            List<HediffComp_Modification> modifications = TargetPawn.health.hediffSet.GetHediffComps<HediffComp_Modification>().ToList();
            for (int i = 0; i < modifications.Count; i++)
            {
                HediffComp_Modification mod = modifications[i];
                if (preciseJob != null)
                {
                    if (!preciseJob.targetHediffDefName.NullOrEmpty() && mod.parent.def?.defName != preciseJob.targetHediffDefName) continue;
                    if (targetPart != null && mod.parent.Part != targetPart) continue;
                    return mod;
                }
                if (mod.isApplyTarget) return mod;
            }
            return null;
        }
    }
}
