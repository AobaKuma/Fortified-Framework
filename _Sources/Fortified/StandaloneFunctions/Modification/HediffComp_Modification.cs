using System;
using System.Collections.Generic;
using Verse;

namespace Fortified
{
    public class HediffComp_Modification : HediffComp
    {
        private string sourceThingDefName;
        private int installedCount = 1;

        public ThingDef SourceThingDef => sourceThingDefName.NullOrEmpty() ? null : DefDatabase<ThingDef>.GetNamedSilentFail(sourceThingDefName);

        public int InstalledCount
        {
            get
            {
                int count = installedCount < 1 ? 1 : installedCount;
                int remaining = RemainingConsumableInstallations();
                return remaining < 0 ? count : Math.Max(1, Math.Min(count, remaining));
            }
        }

        /// <summary>
        /// 消耗性改裝耗損後呼叫，讓記錄的安裝數跟著剩餘耐久下降。
        /// Called when a consumable modification wears down, so the recorded count follows what is left.
        /// </summary>
        public void Notify_Consumed()
        {
            installedCount = InstalledCount;
        }

        // 沒有消耗性元件時回傳 -1。Returns -1 when no comp is consumable.
        private int RemainingConsumableInstallations()
        {
            List<HediffComp> comps = (parent as HediffWithComps)?.comps;
            if (comps == null) return -1;
            int remaining = -1;
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i] is IModificationConsumable consumable)
                {
                    int value = consumable.RemainingInstallations;
                    remaining = remaining < 0 ? value : Math.Min(remaining, value);
                }
            }
            return remaining;
        }

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
