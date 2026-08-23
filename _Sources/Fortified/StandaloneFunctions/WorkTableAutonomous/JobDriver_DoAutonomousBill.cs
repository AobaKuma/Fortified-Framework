using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace Fortified
{
    public class JobDriver_DoAutonomousBill : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Thing thing = job.GetTarget(TargetIndex.A).Thing;
            if (thing == null) return false; ;
            if (!pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed)) return false;
            if (thing != null && thing.def.hasInteractionCell && !pawn.ReserveSittableOrSpot(thing.InteractionCell, job, errorOnFailed))
            {
                return false;
            }
            pawn.ReserveAsManyAsPossible(job.GetTargetQueue(TargetIndex.B), job);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.AddFinishAction(a =>
            {
                if (a != JobCondition.Succeeded && this.TargetThingA is Building_WorkTableAutonomous b)
                {
                    // 只有在機台還沒有進行中的訂單時才清場。開啟 pullFromLinkedStorage 之後，
                    // 機器可能在小人趕路途中自己抽料開工了，這時候 Cancel() 會把別人跑到一半的
                    // 加工進度連同容器裡的料一起倒掉。
                    if (b.activeBill == null)
                    {
                        b.Cancel();
                    }
                }
            });
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);
            this.FailOn(delegate
            {
                if (job.GetTarget(TargetIndex.A).Thing is IBillGiver billGiver)
                {
                    if (job.bill != null && job.bill.DeletedOrDereferenced)
                    {
                        return true;
                    }
                    if (!billGiver.CurrentlyUsableForBills())
                    {
                        return true;
                    }
                }
                // 機台自己抽料開工（pullFromLinkedStorage）時，這趟送料就作廢。
                // 再走下去小人會把第二份原料倒進容器，而 Finish() 是把容器裡的東西
                // 全部當成原料交給 GenRecipe，多的那份會被白吃掉。
                if (job.GetTarget(TargetIndex.A).Thing is Building_WorkTableAutonomous table
                    && table.activeBill != null && table.activeBill != job.bill)
                {
                    return true;
                }
                return false;
            });
            Building_WorkTableAutonomous building = (Building_WorkTableAutonomous)TargetThingA;
            AddEndCondition(delegate
            {
                Thing thing = GetActor().jobs.curJob.GetTarget(TargetIndex.A).Thing;
                return (!(thing is Building) || thing.Spawned) ? JobCondition.Ongoing : JobCondition.Incompletable;
            });
            Toil gotoBillGiver = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            yield return Toils_Jump.JumpIf(gotoBillGiver, () => job.GetTargetQueue(TargetIndex.B).NullOrEmpty());
            foreach (Toil item in JobDriver_DoBill.CollectIngredientsToils(TargetIndex.B, TargetIndex.A, TargetIndex.C, subtractNumTakenFromJobCount: false, failIfStackCountLessThanJobCount: true, placeInBillGiver: true))
            {
                yield return item;
            }
            yield return gotoBillGiver;
            Toil toil = Toils_General.WaitWith(TargetIndex.A, building.GetWorkTime(), useProgressBar: true, maintainPosture: true);
            yield return toil;
            var t = new Toil();
            t.AddPreInitAction(() =>
            {
                building.StartBill((Bill_Production)job.bill, base.TargetThingA, pawn);
            });
            yield return t;
        }
    }
}