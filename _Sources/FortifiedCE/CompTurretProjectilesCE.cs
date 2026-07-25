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
			foreach(AmmoLink link in ammoSet.ammoTypes)
			{
				ammoFilter.SetAllow(link.ammo, true);
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
				if(def != null)
				{
					return Props.ammoSet.ammoTypes.FirstOrDefault(x => x.ammo == def)?.projectile;
				}
				return null;
			}
		}

		public override void InitFromTurret(SubTurret turret)
		{
			base.InitFromTurret(turret);
			if (ammoSettings.NullOrEmpty())
			{
				ThingDef proj = parent.def.Verbs[0].defaultProjectile;
				if (proj != null)
				{
					ThingDef ammo = Props.ammoSet.ammoTypes.FirstOrDefault(x => x.projectile == proj)?.ammo;
					if (ammo != null)
					{
						ammoSettings.SetOrAdd(ammo, Props.defaultAmmoPickUp ?? (parent.def.Verbs[0].burstShotCount * 10));//basic set to prevent empty mechs
					}
				}
			}
		}
	}
}
