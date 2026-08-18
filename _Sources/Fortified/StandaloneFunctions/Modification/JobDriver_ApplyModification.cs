using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Fortified
{
    public class JobDriver_ApplyModification : JobDriver
    {
        private const int DurationTicks = 600;

        private Pawn Target => (Pawn)job.GetTarget(TargetIndex.A).Thing;
        private Thing Item => job.GetTarget(TargetIndex.B).Thing;
        private ThingDef ExpectedItemDef
        {
            get
            {
                Job_Modification precise = job as Job_Modification;
                return precise != null && !precise.itemDefName.NullOrEmpty()
                    ? DefDatabase<ThingDef>.GetNamedSilentFail(precise.itemDefName)
                    : Item?.def;
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (Target == null || !TryResolveAndReserveItem(errorOnFailed)) return false;
            return Target == pawn || pawn.Reserve(Target, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch)
                .FailOnDespawnedOrNull(TargetIndex.B)
                .FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Haul.StartCarryThing(
                TargetIndex.B,
                putRemainderInQueue: false,
                subtractNumTakenFromJobCount: true,
                failIfStackCountLessThanJobCount: true,
                reserve: true,
                canTakeFromInventory: false);
            if (Target != pawn)
            {
                yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
                    .FailOnDespawnedOrNull(TargetIndex.A);
            }

            Toil wait = Toils_General.WaitWith(TargetIndex.A, DurationTicks, true, true);
            wait.FailOnDespawnedOrNull(TargetIndex.A);
            wait.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            wait.WithEffect(EffecterDefOf.MechRepairing, TargetIndex.A);
            wait.handlingFacing = true;
            yield return wait;
            yield return Toils_General.Do(ApplyModification);
        }

        private bool TryResolveAndReserveItem(bool errorOnFailed)
        {
            ThingDef def = ExpectedItemDef;
            if (def == null || pawn?.Map == null) return false;
            Thing current = Item;
            if (IsUsableStack(current, def) && !current.IsForbidden(pawn)
                && pawn.CanReach(current, PathEndMode.Touch, Danger.Deadly) && pawn.CanReserve(current))
            {
                job.count = 1;
                return pawn.Reserve(current, job, 1, -1, null, errorOnFailed);
            }

            MapComponent_ModificationIndex index = pawn.Map.GetComponent<MapComponent_ModificationIndex>();
            List<Thing> candidates = index?.GetCandidates(def, pawn, pawn.Position, true);
            if (candidates == null) return false;
            for (int i = 0; i < candidates.Count; i++)
            {
                Thing candidate = candidates[i];
                if (!pawn.Reserve(candidate, job, 1, -1, null, false)) continue;
                job.SetTarget(TargetIndex.B, candidate);
                job.count = 1;
                return true;
            }
            return false;
        }

        private static bool IsUsableStack(Thing thing, ThingDef def)
        {
            return thing != null && !thing.Destroyed && thing.Spawned && thing.def == def && thing.stackCount > 0;
        }

        private void ApplyModification()
        {
            Thing item = pawn.carryTracker?.CarriedThing;
            if (item == null || item.def != ExpectedItemDef || !ModificationForPawn(Target, item))
            {
                ReturnCarriedItem();
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            item.SplitOff(1).Destroy(DestroyMode.Vanish);
            EndJobWith(JobCondition.Succeeded);
        }

        private bool ModificationForPawn(Pawn target, Thing item)
        {
            CompTargetable_AddHediffOnTarget comp = item?.TryGetComp<CompTargetable_AddHediffOnTarget>();
            ModificationProfile profile = ModificationProfileDatabase.Get(item?.def);
            if (target?.health?.hediffSet == null || comp?.Props?.hediffDef == null || profile == null) return false;

            Job_Modification preciseJob = job as Job_Modification;
            BodyPartRecord part;
            if (preciseJob != null && (preciseJob.targetPartIndex >= 0 || !preciseJob.targetPartDefName.NullOrEmpty()))
            {
                part = preciseJob.ResolvePart(target);
            }
            else if (!ModificationInstallValidator.TryFindInstallPart(target, item.def, null, out part, out _, false))
            {
                return false;
            }
            bool allowEquivalentPart = preciseJob?.allowEquivalentPart == true;
            if (!ModificationInstallValidator.CanInstall(target, item.def, part, null, out _, false, allowEquivalentPart)) return false;

            Hediff incoming = HediffMaker.MakeHediff(profile.hediffDef, target, part);
            incoming.TryGetComp<HediffComp_Modification>()?.SetSource(item.def);
            Hediff existing = FindExisting(target, profile, part);
            if (existing != null)
            {
                if (!profile.mergeable || !ModificationProfileDatabase.CanMerge(existing, incoming) || !existing.TryMergeWith(incoming)) return false;
            }
            else
            {
                target.health.AddHediff(incoming);
            }

            comp.Props.soundDef?.PlayOneShot(SoundInfo.InMap(target));
            Messages.Message("FFF.Message.Modification.Applied".Translate(target), target, MessageTypeDefOf.PositiveEvent);
            return true;
        }

        private static Hediff FindExisting(Pawn target, ModificationProfile profile, BodyPartRecord part)
        {
            List<Hediff> hediffs = target.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff?.def != profile.hediffDef) continue;
                if (!profile.TargetsBodyPart || hediff.Part == part) return hediff;
            }
            return null;
        }

        private void ReturnCarriedItem()
        {
            if (pawn?.carryTracker?.CarriedThing == null || !pawn.Spawned) return;
            pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
        }
    }
}
