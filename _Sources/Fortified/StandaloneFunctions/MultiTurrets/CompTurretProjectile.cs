using Multiplayer.API;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions.Must;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;
using static UnityEngine.Networking.UnityWebRequest;

namespace Fortified
{
	public class CompProperties_TurretProjectile : CompProperties_ChangeableProjectile
	{
		public ThingFilter ammoFilter;

		public IntRange startingAmmoRange;

		public CompProperties_TurretProjectile()
		{
			compClass = typeof(CompTurretProjectile);
		}

		public override void ResolveReferences(ThingDef parentDef)
		{
			base.ResolveReferences(parentDef);
			ammoFilter.ResolveReferences();
			if(parentDef.building == null)
			{
				parentDef.building = new BuildingProperties();
				parentDef.building.fixedStorageSettings = new StorageSettings();
				parentDef.building.fixedStorageSettings.filter = new ThingFilter();
				parentDef.building.fixedStorageSettings.filter.CopyAllowancesFrom(ammoFilter);
				parentDef.building.defaultStorageSettings = new StorageSettings();
				parentDef.building.defaultStorageSettings.CopyFrom(parentDef.building.fixedStorageSettings);
			}
		}

		public virtual IEnumerable<ThingDef> AllAcceptedAmmo()
		{
			return ammoFilter.AllowedThingDefs;
		}

		public virtual bool AcceptsAmmo(ThingDef ammo)
		{
			bool flag = ammoFilter.Allows(ammo);
			return flag;
		}
	}

	public class CompTurretProjectile : CompChangeableProjectile
	{
		public new CompProperties_TurretProjectile Props => (CompProperties_TurretProjectile)props;

		public SubTurret turret;

		public ThingDef selectedAmmoDef;

		public Dictionary<ThingDef, int> ammoSettings = new Dictionary<ThingDef, int>();

		// Two methods below should NEVER use base ones, it causes crushes!!!

		public virtual ThingDef LoadedShellOverride
		{
			get
			{
				if(turret?.PawnOwner?.inventory?.innerContainer?.NullOrEmpty() != false)
				{
					return null;
				}
				if (selectedAmmoDef == null)
				{
					ThingDef def = turret.PawnOwner.inventory.innerContainer.FirstOrDefault(x => Props.AcceptsAmmo(x.def))?.def;
					return def;
				}
				return turret.PawnOwner.inventory.innerContainer.FirstOrDefault(x => x.def == selectedAmmoDef)?.def;
			}
		}

		public virtual ThingDef ProjectileOverride => LoadedShell?.projectileWhenLoaded;

		public virtual void InitFromTurret(SubTurret turret)
		{
			this.turret = turret;
			//selectedAmmoDef = ThingDefOf.Shell_HighExplosive;
		}

		public virtual void PostGenInit(Pawn pawn)
		{
			int count = Props.startingAmmoRange.RandomInRange;
			if (count > 0)
			{
				ThingDef def = Props.AllAcceptedAmmo().RandomElementByWeight(x => x.generateCommonality);
				while(count > 0)
				{
					Thing t = ThingMaker.MakeThing(def);
					t.stackCount = Mathf.Min(count, def.stackLimit);
					count -= t.stackCount;
					pawn.inventory.innerContainer.TryAddOrTransfer(t);
				}
			}
		}

		public override void Notify_UsedWeapon(Pawn pawn)
		{
			Thing ammo;
			if (selectedAmmoDef == null)
			{
				ammo = pawn.inventory.innerContainer.FirstOrDefault(x => Props.AcceptsAmmo(x.def));
			}
			else
			{
				ammo = pawn.inventory.innerContainer.FirstOrDefault(x => x.def == selectedAmmoDef);
			}
			if (ammo == null)
			{
				turret.RunOutOfAmmo();
				return;
			}
			if (ammo.stackCount > 1)
			{
				ammo.SplitOff(1).Destroy();
			}
			else
			{
				pawn.inventory.innerContainer.Remove(ammo);
				ammo.Destroy();
				ammo = null;
				if (OutOfAmmo())
				{
					turret.RunOutOfAmmo();
					return;
				}
			}
		}

		public override void Notify_ProjectileLaunched()
		{
			
		}

		public virtual bool OutOfAmmo()
		{
			if (turret.PawnOwner.inventory.innerContainer.NullOrEmpty())
			{
				return true;
			}
			if(LoadedShell == null)
			{
				return true;
			}
			return false;
		}

		public override void PostExposeData()
		{
			base.PostExposeData();
			Scribe_Defs.Look(ref selectedAmmoDef, "selectedAmmoDef");
			Scribe_Collections.Look(ref ammoSettings, "ammoSettings");
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				if (ammoSettings == null)
				{
					ammoSettings = new Dictionary<ThingDef, int>();
				}
			}
		}
	}
}