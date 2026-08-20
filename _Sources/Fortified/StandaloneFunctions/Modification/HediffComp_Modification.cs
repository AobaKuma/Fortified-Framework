using Verse;

namespace Fortified
{
    public class HediffComp_Modification : HediffComp
    {
        public bool isApplyTarget = false;
        private string sourceThingDefName;
        private int installedCount = 1;

        public ThingDef SourceThingDef => sourceThingDefName.NullOrEmpty() ? null : DefDatabase<ThingDef>.GetNamedSilentFail(sourceThingDefName);
        public int InstalledCount => installedCount < 1 ? 1 : installedCount;

        public HediffCompProperties_Modification Props
        {
            get
            {
                return (HediffCompProperties_Modification)props;
            }
        }

        public void SetSource(ThingDef source)
        {
            if (source != null) sourceThingDefName = source.defName;
        }

        public override void CompPostMerged(Hediff other)
        {
            base.CompPostMerged(other);
            HediffComp_Modification otherComp = other?.TryGetComp<HediffComp_Modification>();
            installedCount += otherComp?.InstalledCount ?? 1;
            if (sourceThingDefName.NullOrEmpty() && otherComp != null) sourceThingDefName = otherComp.sourceThingDefName;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref sourceThingDefName, "sourceThingDefName");
            Scribe_Values.Look(ref installedCount, "installedCount", 1);
            if (installedCount < 1) installedCount = 1;
        }
    }
    public class HediffCompProperties_Modification : HediffCompProperties
    {
        public HediffCompProperties_Modification()
        {
            compClass = typeof(HediffComp_Modification);
        }
        public JobDef applyJob;
    }
}
