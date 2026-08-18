using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace Fortified
{
    public class Job_Modification : Job, IExposable
    {
        public string targetPartDefName;
        public int targetPartIndex = -1;
        public string targetHediffDefName;
        public string itemDefName;
        public bool allowEquivalentPart;

        void IExposable.ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref targetPartDefName, "targetPartDefName");
            Scribe_Values.Look(ref targetPartIndex, "targetPartIndex", -1);
            Scribe_Values.Look(ref targetHediffDefName, "targetHediffDefName");
            Scribe_Values.Look(ref itemDefName, "itemDefName");
            Scribe_Values.Look(ref allowEquivalentPart, "allowEquivalentPart", false);
        }

        public BodyPartRecord ResolvePart(Pawn pawn)
        {
            List<BodyPartRecord> parts = pawn?.RaceProps?.body?.AllParts;
            if (parts == null) return null;
            if (targetPartIndex >= 0 && targetPartIndex < parts.Count)
            {
                BodyPartRecord indexed = parts[targetPartIndex];
                if (targetPartDefName.NullOrEmpty() || indexed.def?.defName == targetPartDefName) return indexed;
            }
            return targetPartDefName.NullOrEmpty() ? null : parts.FirstOrDefault(p => p.def?.defName == targetPartDefName);
        }
    }

    public static class ModificationJobUtility
    {
        public static Job_Modification MakeApplyJob(JobDef def, Pawn target, Thing item, BodyPartRecord part)
        {
            Job_Modification job = MakeJob(def, target, part);
            job.SetTarget(TargetIndex.B, item);
            job.itemDefName = item?.def?.defName;
            job.count = 1;
            return job;
        }

        public static Job_Modification MakeRemoveJob(JobDef def, Pawn target, Hediff hediff)
        {
            Job_Modification job = MakeJob(def, target, hediff?.Part);
            job.targetHediffDefName = hediff?.def?.defName;
            return job;
        }

        private static Job_Modification MakeJob(JobDef def, Pawn target, BodyPartRecord part)
        {
            Job_Modification job = new Job_Modification
            {
                def = def,
                targetPartDefName = part?.def?.defName,
                targetPartIndex = part?.Index ?? -1,
                playerForced = true
            };
            job.SetTarget(TargetIndex.A, target);
            return job;
        }
    }
}
