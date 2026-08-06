using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Fortified
{

	public class JobDriver_UseAccessKey : JobDriver
	{
		private Thing ActivatableThing => job.GetTarget(TargetIndex.A).Thing;

		private CompAccessKeyActivatable Comp
		{
			get
			{
				return job.GetTarget(TargetIndex.A).Thing.TryGetComp<CompAccessKeyActivatable>();
			}
		}

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			if (base.TargetThingB != null)
			{
				pawn.ReserveAsManyAsPossible(job.GetTargetQueue(TargetIndex.B), job);
			}
			if (base.TargetThingB != null && !pawn.Reserve(job.GetTarget(TargetIndex.B), job, 1, -1, null, errorOnFailed))
			{
				return false;
			}
			return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed);
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			if (job.GetTarget(TargetIndex.C).HasThing)
			{
				yield return TakeItemFromInventoryToCarrier(pawn, TargetIndex.C);
			}
			if (job.GetTarget(TargetIndex.B).HasThing)
			{
				Toil opt = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch, canGotoSpawnedParent: true).FailOnDespawnedNullOrForbidden(TargetIndex.B).FailOnSomeonePhysicallyInteracting(TargetIndex.B)
					.FailOn(() => !Comp.CanActivate(pawn));
				yield return opt;
				yield return Toils_Haul.StartCarryThing(TargetIndex.B, putRemainderInQueue: false, subtractNumTakenFromJobCount: true, failIfStackCountLessThanJobCount: false, reserve: true, canTakeFromInventory: true);
				yield return Toils_Haul.JumpIfAlsoCollectingNextTargetInQueue(opt, TargetIndex.B);
			}
			yield return Toils_Goto.GotoThing(TargetIndex.A, ActivatableThing.def.HasSingleOrMultipleInteractionCells ? PathEndMode.InteractionCell : PathEndMode.Touch).FailOnDespawnedNullOrForbidden(TargetIndex.A).FailOnSomeonePhysicallyInteracting(TargetIndex.A)
				.FailOn(() => !Comp.CanActivate(pawn));
			if (Comp.TicksToActivate != 0)
			{
				int num = Comp.TicksToActivate;
				int remainingTicks = Mathf.RoundToInt((float)num * (1f - Comp.progress));
				yield return WaitForActivate(remainingTicks, num);
			}
			yield return Toils_General.Do(delegate
			{
				if(pawn.carryTracker.CarriedThing.stackCount > Comp.Props.accessKeyCount)
				{
					pawn.carryTracker.CarriedThing.SplitOff(Comp.Props.accessKeyCount).Destroy();
				}
				else
				{
					pawn.carryTracker.DestroyCarriedThing();
				}
				Comp.Activate(pawn);
			});
		}

		private Toil TakeItemFromInventoryToCarrier(Pawn pawn, TargetIndex itemInd)
		{
			Toil toil = ToilMaker.MakeToil("TakeItemFromInventoryToCarrier");
			toil.initAction = delegate
			{
				Job curJob = pawn.CurJob;
				Thing thing = (Thing)curJob.GetTarget(itemInd);
				int count = Mathf.Min(thing.stackCount, curJob.count);
				pawn.inventory.innerContainer.TryTransferToContainer(thing, pawn.carryTracker.innerContainer, count);
				curJob.SetTarget(itemInd, pawn.carryTracker.CarriedThing);
				job.count -= count;
			};
			return toil;
		}

		private Toil WaitForActivate(int remainingTicks, int totalTicks)
		{
			Toil toil = ToilMaker.MakeToil("WaitForActivate").FailOn(() => !Comp.CanActivate(pawn));
			toil.WithProgressBarToilDelay(TargetIndex.A, remainingTicks);
			Toil toil2 = toil;
			toil2.initAction = (Action)Delegate.Combine(toil2.initAction, (Action)delegate
			{
				toil.actor.pather.StopDead();
			});
			Toil toil3 = toil;
			toil3.tickIntervalAction = (Action<int>)Delegate.Combine(toil3.tickIntervalAction, (Action<int>)delegate
			{
				pawn.rotationTracker.FaceTarget(base.TargetA);
			});
			toil.handlingFacing = true;
			toil.defaultCompleteMode = ToilCompleteMode.Delay;
			toil.socialMode = RandomSocialMode.Off;
			toil.defaultDuration = remainingTicks;
			return toil;
		}
	}
}
