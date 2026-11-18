using RimWorld;
using Verse;
using Verse.AI;

namespace Fortified
{
	/// <summary>
	/// 軍用人工生物（AMO）基底：預設「狀態可控」、
	/// 不可被機械師/監督者修理、自體再生、以口糧補能量。
	/// 以 Pawn + IWeaponUsable 為基底（與 WeaponUsableMech / HumanlikeMech 同層）。
	/// </summary>
	public class ArtificialOrganism : Pawn, IWeaponUsable, IStateControllableMech, ICachedMechComps, ICaravanOwner
	{
		public virtual bool CanCaravan => false;
		public bool ControllableByState => true; // 預設 true，無需額外檢查

		/// <summary>
		/// AMO 不可被機械師/監督者修理（自行再生）。def 亦應移除 CompMechRepairable；
		/// 此屬性作為 Framework 層防呆，即使 def 遺漏也會擋下修理判定。
		/// </summary>
		public virtual bool Repairable => false;

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
			// 預先填值快取 comps（comp 在 spawn 後由 def 固定，不會再變）
			cachedOverseerSubject = GetComp<CompOverseerSubject>();
			cachedDeadManSwitch = GetComp<CompDeadManSwitch>();
			cachedCommandRelay = GetComp<CompCommandRelay>();
			cachedDrone = GetComp<CompDrone>();
			cachedMechRepairable = GetComp<CompMechRepairable>();
			if (!respawningAfterLoad)
			{
				TryRestoreOverseerAfterResurrection();
			}
		}

		/// <summary>
		/// 原版 Overseer 關係為 removeOnDeath，死亡即解除；原版只有培育器的復活帳單會重新綁定。
		/// AMO 走其他復活途徑（血清、FFF 復活、dev）時會變成無監管者，連帶失去徵召／工作模式面板。
		/// 這裡記住死前的監管者，復活後首次 spawn 時重新綁回。
		/// </summary>
		private Pawn overseerBeforeDeath;

		public override void Kill(DamageInfo? dinfo, Hediff exactCulprit = null)
		{
			if (!Dead)
			{
				overseerBeforeDeath = this.GetOverseer();
			}
			base.Kill(dinfo, exactCulprit);
		}

		private void TryRestoreOverseerAfterResurrection()
		{
			Pawn overseer = overseerBeforeDeath;
			if (overseer == null || Dead)
			{
				return;
			}
			overseerBeforeDeath = null;
			// 已由其他途徑（例如培育器復活帳單）綁定監管者時不重複加。
			if (this.GetOverseer() != null || OverseerSubjectComp == null)
			{
				return;
			}
			if (overseer.Dead || overseer.Destroyed || overseer.relations == null || !MechanitorUtility.IsMechanitor(overseer))
			{
				return;
			}
			if (Faction == null || overseer.Faction != Faction)
			{
				return;
			}
			overseer.relations.AddDirectRelation(PawnRelationDefOf.Overseer, this);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			if (Scribe.mode == LoadSaveMode.Saving && overseerBeforeDeath != null && (overseerBeforeDeath.Discarded || overseerBeforeDeath.Destroyed))
			{
				overseerBeforeDeath = null;
			}
			Scribe_References.Look(ref overseerBeforeDeath, "overseerBeforeDeath");
		}

		// —— IWeaponUsable 實作（與 WeaponUsableMech 相同簽名）——
		public void Equip(ThingWithComps equipment)
		{
			equipment.SetForbidden(false);
			jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Equip, equipment), JobTag.DraftedOrder);
		}

		public void Wear(ThingWithComps apparel)
		{
			apparel.SetForbidden(false);
			jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wear, apparel), JobTag.DraftedOrder);
		}
	}
}
