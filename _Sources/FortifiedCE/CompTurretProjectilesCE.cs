using CombatExtended;
using Fortified;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace FortifiedCE
{
	public class CompProperties_TurretProjectileCE : CompProperties_TurretProjectile
	{
		public CombatExtended.AmmoSetDef ammoSet;

		public int? defaultAmmoPickUp = null;

		public CompProperties_TurretProjectileCE()
		{
			compClass = typeof(CompTurretProjectileCE);
		}

		public override void ResolveReferences(ThingDef parentDef)
		{
			ammoFilter = new ThingFilter();
			if (ammoSet?.ammoTypes != null)
			{
				foreach (AmmoLink link in ammoSet.ammoTypes)
				{
					if (link?.ammo != null)
					{
						ammoFilter.SetAllow(link.ammo, true);
					}
				}
			}
			else
			{
				Log.Error($"[FFF] {parentDef?.defName} has CompProperties_TurretProjectileCE without a valid ammoSet.");
			}
			base.ResolveReferences(parentDef);
		}
	}

	public class CompTurretProjectileCE : CompTurretProjectile
	{
		public new CompProperties_TurretProjectileCE Props => (CompProperties_TurretProjectileCE)props;

		public override ThingDef ProjectileOverride
		{
			get
			{
				ThingDef def = LoadedShellOverride;
				if (def != null && Props.ammoSet?.ammoTypes != null)
				{
					return Props.ammoSet.ammoTypes.FirstOrDefault(x => x.ammo == def)?.projectile;
				}
				return null;
			}
		}

		/// <summary>
		/// Combat Extended leaves generateCommonality at 0 on ammo so it stays out of vanilla loot rolls, and weights
		/// its own ammo rolls by generateAllowChance instead (see LoadoutPropertiesExtension.LoadWeaponWithRandAmmo).
		/// Mirror that here, otherwise the base weighted pick has a total weight of 0 and hands back nothing.
		/// </summary>
		public override ThingDef SelectStartingAmmo()
		{
			if (Props.ammoSet?.ammoTypes.NullOrEmpty() != false)
			{
				return base.SelectStartingAmmo();
			}
			List<AmmoDef> candidates = Props.ammoSet.ammoTypes
				.Where(x => x?.ammo != null && x.ammo.alwaysHaulable && !x.ammo.menuHidden && x.ammo.generateAllowChance > 0f)
				.Select(x => x.ammo)
				.ToList();
			if (candidates.Count == 0)
			{
				return base.SelectStartingAmmo();
			}
			if (candidates.TryRandomElementByWeight(x => x.generateAllowChance, out AmmoDef result) && result != null)
			{
				return result;
			}
			return candidates.RandomElement();
		}

		public override void InitFromTurret(SubTurret turret)
		{
			base.InitFromTurret(turret);
			if (ammoSettings.NullOrEmpty())
			{
				VerbProperties verb = parent?.def?.Verbs?.FirstOrDefault();
				if (verb == null || Props.ammoSet?.ammoTypes == null)
				{
					return;
				}
				ThingDef proj = verb.defaultProjectile;
				if (proj != null)
				{
					ThingDef ammo = Props.ammoSet.ammoTypes.FirstOrDefault(x => x.projectile == proj)?.ammo;
					if (ammo != null)
					{
						ammoSettings.SetOrAdd(ammo, Props.defaultAmmoPickUp ?? (verb.burstShotCount * 10));//basic set to prevent empty mechs
					}
				}
			}
		}
	}
}
