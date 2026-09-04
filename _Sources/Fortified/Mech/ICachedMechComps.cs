using RimWorld;
using Verse;

namespace Fortified
{
	/// <summary>
	/// 快取 comps 介面：WeaponUsableMech / HumanlikeMech / ArtificialOrganism 皆實作，
	/// 讓 patch 以單一介面存取快取的 comps，取代散落的 TryGetComp 查詢。
	/// </summary>
	public interface ICachedMechComps
	{
		CompOverseerSubject OverseerSubjectComp { get; }

		CompDeadManSwitch DeadManSwitchComp { get; }

		CompCommandRelay CommandRelayComp { get; }

		CompDrone DroneComp { get; }

		CompMechRepairable MechRepairableComp { get; }
	}

	/// <summary>
	/// 取這些 comps 的統一入口。
	/// 框架自訂的 thingClass（實作 ICachedMechComps）走快取欄位，
	/// 其他 thingClass（外部模組多半直接用原版 Verse.Pawn）回退 TryGetComp。
	/// IMPORTANT: 千萬別在判斷式裡只寫 <c>x is ICachedMechComps</c> —— comps 是掛在 def 上的，
	/// 跟 thingClass 無關，那樣寫會讓所有非框架 thingClass 的機械體整條分支失效。
	/// </summary>
	public static class CachedMechCompsUtility
	{
		public static CompOverseerSubject CachedOverseerSubject(this Thing thing)
		{
			return thing is ICachedMechComps cc ? cc.OverseerSubjectComp : thing.TryGetComp<CompOverseerSubject>();
		}

		public static CompDeadManSwitch CachedDeadManSwitch(this Thing thing)
		{
			return thing is ICachedMechComps cc ? cc.DeadManSwitchComp : thing.TryGetComp<CompDeadManSwitch>();
		}

		public static CompCommandRelay CachedCommandRelay(this Thing thing)
		{
			return thing is ICachedMechComps cc ? cc.CommandRelayComp : thing.TryGetComp<CompCommandRelay>();
		}

		public static CompDrone CachedDrone(this Thing thing)
		{
			return thing is ICachedMechComps cc ? cc.DroneComp : thing.TryGetComp<CompDrone>();
		}

		public static CompMechRepairable CachedMechRepairable(this Thing thing)
		{
			return thing is ICachedMechComps cc ? cc.MechRepairableComp : thing.TryGetComp<CompMechRepairable>();
		}
	}
}
