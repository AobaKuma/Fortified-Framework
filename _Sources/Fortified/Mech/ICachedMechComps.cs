using RimWorld;

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
}
