using Verse;

namespace Fortified;

/// <summary>
/// <see cref="Verb_Shoot"/> flavoured counterpart of <see cref="Verb_LaunchProjectile_AmmoSwitch"/>.
/// <para>
/// Prefer this class over <see cref="Verb_LaunchProjectile_AmmoSwitch"/> for any weapon a humanlike
/// pawn can equip: Verb_Shoot is what grants Shooting skill XP on warmup completion and increments the
/// ShotsFired record, and deriving straight from Verb_LaunchProjectile silently drops both.
/// Verb_LaunchProjectile_AmmoSwitch remains the correct choice for turrets and mech-only weaponry,
/// where neither behaviour applies.
/// </para>
/// </summary>
public class Verb_Shoot_AmmoSwitch : Verb_Shoot
{
	private CompAmmoSwitch compInt;

	public CompAmmoSwitch Comp
	{
		get
		{
			var eq = EquipmentSource;
			if (compInt == null || compInt.parent != eq)
			{
				compInt = eq?.TryGetComp<CompAmmoSwitch>();
			}
			return compInt;
		}
	}

	public override ThingDef Projectile
	{
		get
		{
			CompAmmoSwitch comp = Comp;
			if (comp?.CurrentProjectile != null)
				return comp.CurrentProjectile;

			return base.Projectile;
		}
	}

	protected override int ShotsPerBurst => Comp?.CurrentAmmo?.burstShotCountOverride ?? base.BurstShotCount;

	public override float WarmupTime => base.WarmupTime * (Comp?.CurrentAmmo?.warmUpFactor ?? 1f);

	public override float EffectiveRange => base.EffectiveRange * (Comp?.CurrentAmmo?.rangeFactor ?? 1f);

	public override bool Available()
	{
		if (!base.Available()) return false;

		CompAmmoSwitch comp = Comp;
		if (comp != null && comp.IsOnSwitchCooldown && state != VerbState.Bursting)
			return false;

		return true;
	}

	public override bool TryStartCastOn(
		LocalTargetInfo castTarg,
		LocalTargetInfo destTarg,
		bool surpriseAttack = false,
		bool canHitNonTargetPawns = true,
		bool preventFriendlyFire = false,
		bool nonInterruptingSelfCast = false)
	{
		var comp = Comp;
		if (comp != null && comp.IsOnSwitchCooldown && state != VerbState.Bursting)
			return false;

		return base.TryStartCastOn(castTarg, destTarg, surpriseAttack, canHitNonTargetPawns, preventFriendlyFire, nonInterruptingSelfCast);
	}
}
