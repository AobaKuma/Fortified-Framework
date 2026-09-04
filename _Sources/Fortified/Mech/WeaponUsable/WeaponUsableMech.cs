using System.Collections.Generic;
using Verse;
using RimWorld;
using UnityEngine;
using Verse.AI;
using System.Linq;
using Fortified;

namespace Fortified
{
    public class WeaponUsableMech : Pawn, IWeaponUsable, ICaravanOwner, ICachedMechComps
    {
		public virtual bool CanCaravan => false;
		public MechWeaponExtension MechWeapon { get; private set; }

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

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            MechWeapon = def.GetModExtension<MechWeaponExtension>();
            interactions ??= new(this);
            inventory ??= new(this);
            equipment ??= new(this);
            skills ??= new(this);
            skills.skills.ForEach(s => s.Level = def.race.mechFixedSkillLevel == 0 ? 5 : def.race.mechFixedSkillLevel);

            // 預先填值快取 comps（comp 在 spawn 後由 def 固定，不會再變）
            cachedOverseerSubject = GetComp<CompOverseerSubject>();
            cachedDeadManSwitch = GetComp<CompDeadManSwitch>();
            cachedCommandRelay = GetComp<CompCommandRelay>();
            cachedDrone = GetComp<CompDrone>();
            cachedMechRepairable = GetComp<CompMechRepairable>();

            // 初始化工作设置，让机械体能够被分配工作
            if (workSettings == null)
            {
                workSettings = new Pawn_WorkSettings(this);
                workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                // 限制機械體工作
                if (!this.RaceProps.IsMechanoid && !this.RaceProps.mechEnabledWorkTypes.NullOrEmpty())
                {
                    foreach (WorkTypeDef w in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                    {
                        if (!this.RaceProps.mechEnabledWorkTypes.Contains(w))
                        {
                            workSettings.SetPriority(w, 0);
                        }
                    }
                }
            }
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
                Drawer?.renderer?.SetAllGraphicsDirty();
            }
        }
        public void Equip(ThingWithComps equipment)
        {
            equipment.SetForbidden(false);
            jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Equip, equipment), JobTag.DraftedOrder);
        }
        public void Wear(ThingWithComps apparel)
        {
            apparel.SetForbidden(false);
            this.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wear, apparel), JobTag.DraftedOrder);
        }
    }
}
