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
	public class ArtificialOrganism : Pawn, IWeaponUsable, IStateControllableMech
	{
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
