using System.Collections.Generic;

using Verse;
using RimWorld;
using UnityEngine;
using Verse.AI;
using System.Linq;

namespace Fortified
{
    public class HumanlikeMech : Pawn, IWeaponUsable, ICaravanOwner, IHumanlikeMech, ICachedMechComps
    {
		public virtual bool CanCaravan => false;//Maybe should be true?????

		public MechWeaponExtension MechWeapon => def.GetModExtension<MechWeaponExtension>();
        public HumanlikeMechExtension Extension => HumanlikeMechUtility.Extension(this);

		// —— 快取 comps（避免 patch 熱路徑重複 TryGetComp）——
		private CompOverseerSubject cachedOverseerSubject;
		private CompDeadManSwitch cachedDeadManSwitch;
		private CompCommandRelay cachedCommandRelay;
		private CompDrone cachedDrone;
		private CompMechRepairable cachedMechRepairable;

		public CompOverseerSubject OverseerSubjectComp => cachedOverseerSubject ??= GetComp<CompOverseerSubject>();
		public CompDeadManSwitch DeadManSwitchComp => cachedDeadManSwitch ??= GetComp<CompDeadManSwitch>();
		public CompCommandRelay CommandRelayComp => cachedCommandRelay ??= GetComp<CompCommandRelay>();
		public CompDrone DroneComp => cachedDrone ??= GetComp<CompDrone>();
		public CompMechRepairable MechRepairableComp => cachedMechRepairable ??= GetComp<CompMechRepairable>();

        public Graphic HeadGraphic => HumanlikeMechUtility.GetHeadGraphic(this);

        public override void PostMake()
        {
            base.PostMake();
            HumanlikeMechUtility.CheckTracker(this);
        }
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            // 預先填值快取 comps（comp 在 spawn 後由 def 固定，不會再變）
            cachedOverseerSubject = GetComp<CompOverseerSubject>();
            cachedDeadManSwitch = GetComp<CompDeadManSwitch>();
            cachedCommandRelay = GetComp<CompCommandRelay>();
            cachedDrone = GetComp<CompDrone>();
            cachedMechRepairable = GetComp<CompMechRepairable>();
            HumanlikeMechUtility.CheckTracker(this);
        }
        public override void Kill(DamageInfo? dinfo, Hediff exactCulprit = null)
        {
            if (dinfo == null)//解體殺
            {
                List<Hediff> hediffs = health.hediffSet.hediffs.Where(h => h.def.spawnThingOnRemoved != null).ToList();
                foreach (Hediff item in hediffs)
                {
                    health.RemoveHediff(item);
                    Thing thing = ThingMaker.MakeThing(item.def.spawnThingOnRemoved);
                    thing.stackCount = 1;
                    GenPlace.TryPlaceThing(thing, this.Position, this.Map, ThingPlaceMode.Near);
                }
            }
            base.Kill(dinfo, exactCulprit);
        }
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                HumanlikeMechUtility.CheckTracker(this);
                this.Drawer?.renderer?.SetAllGraphicsDirty();
            }
        }

        // 限制机械体工作类型（保留 public API，供 Patch_PawnGenerator_GeneratePawn 使用）
        public void ApplyWorkTypeRestrictions() => HumanlikeMechUtility.ApplyWorkTypeRestrictions(this);

        public void Equip(ThingWithComps equipment)
        {
            equipment.SetForbidden(false);
            this.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Equip, equipment), JobTag.DraftedOrder);
        }

        public void Wear(ThingWithComps apparel)
        {
            apparel.SetForbidden(false);
            this.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wear, apparel), JobTag.DraftedOrder);
        }
    }
}
