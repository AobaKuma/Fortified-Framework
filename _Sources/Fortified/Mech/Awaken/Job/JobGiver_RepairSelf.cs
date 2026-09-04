using Fortified;
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
    public class JobGiver_RepairSelf : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            //AMO 不可被修理（自行再生，走 CompArtificialOrganism）。
            if (pawn is ArtificialOrganism amo && !amo.Repairable) return null;
            if (pawn.CachedDeadManSwitch()?.woken == true && MechRepairUtility.CanRepair(pawn))
            {
                return JobMaker.MakeJob(FFF_DefOf.FFF_RepairSelf,pawn);
            }
            return null;
        }
    }
}
