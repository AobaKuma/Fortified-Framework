using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;

namespace Fortified
{
    public class WorkGiver_DoAutonomousBill : WorkGiver_DoBill
    {
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return base.HasJobOnThing(pawn, t, forced) && pawn.CanReserveAndReach(t, PathEndMode.InteractionCell, Danger.Deadly) && t is Building_WorkTableAutonomous;
        }

        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (thing is Building_WorkTableAutonomous building)
            {
                if (building.activeBill == null)
                {
                    Job job = base.JobOnThing(pawn, thing, forced);
                    if (job == null) return null;

                    // WorkGiver_DoBill.TryStartNewDoBillJob 在工作台格子上有雜物時，
                    // 回傳的是「把東西搬開」的清場工作而不是 DoBill。
                    // 那種工作硬轉成 FFF_DoAutonomousBill 會得到一個 bill 為 null 的空殼，
                    // JobDriver 跑完只會呼叫 StartBill(null) 直接 return ——
                    // 雜物永遠不會被搬走，機器也就永遠卡在開不了工的狀態。
                    // 這裡原封不動放行，讓小人先清場，下一次派工自然就會拿到真正的 DoBill。
                    if (job.def != JobDefOf.DoBill || job.bill == null) return job;

                    Job job2 = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("FFF_DoAutonomousBill"), thing);
                    job2.targetQueueA = job.targetQueueA;
                    job2.targetQueueB = job.targetQueueB;
                    job2.countQueue = job.countQueue;
                    job2.haulMode = HaulMode.ToCellNonStorage;
                    job2.bill = job.bill;
                    return job2;
                }
                else if (!building.prepared && building.activeBill.recipe.PawnSatisfiesSkillRequirements(pawn))
                {
                    return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("FFF_FinishAutonomousBill"), thing);
                }
            }
            return null;
        }
    }
}