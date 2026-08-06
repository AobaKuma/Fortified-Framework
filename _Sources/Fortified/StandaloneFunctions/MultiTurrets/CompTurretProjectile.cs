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
			if (ammoFilter == null)
			{
				Log.Error($"[FFF] {parentDef?.defName} has CompProperties_TurretProjectile without an ammoFilter.");
				ammoFilter = new ThingFilter();
			}
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
			return ammoFilter?.AllowedThingDefs ?? Enumerable.Empty<ThingDef>();
		}

		public virtual bool AcceptsAmmo(ThingDef ammo)
		{
			bool flag = ammo != null && ammoFilter != null && ammoFilter.Allows(ammo);
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

		/// <summary>
		/// Picks which ammo a freshly generated pawn spawns with.
		/// Never returns a def that cannot be instantiated; returns null when there is nothing sane to hand out.
		/// </summary>
		public virtual ThingDef SelectStartingAmmo()
		{
			List<ThingDef> candidates = Props.AllAcceptedAmmo()?.Where(x => x != null).ToList();
			if (candidates.NullOrEmpty())
			{
				return null;
			}
			// generateCommonality is 0 on plenty of ammo defs (Combat Extended zeroes it so ammo stays out of
			// vanilla loot rolls). RandomElementByWeight returns null on a zero total weight, so fall back to a
			// uniform pick instead of handing a null def to ThingMaker.
			if (candidates.TryRandomElementByWeight(x => Mathf.Max(x.generateCommonality, 0f), out ThingDef weighted) && weighted != null)
			{
				return weighted;
			}
			return candidates.RandomElement();
		}

		public virtual void PostGenInit(Pawn pawn)
		{
			if (pawn?.inventory?.innerContainer == null)
			{
				return;
			}
			int count = Props.startingAmmoRange.RandomInRange;
			if (count <= 0)
			{
				return;
			}
			ThingDef def = SelectStartingAmmo();
			if (def == null)
			{
				return;
			}
			int stackLimit = Mathf.Max(1, def.stackLimit);
			while (count > 0)
			{
				Thing t = ThingMaker.MakeThing(def);
				if (t == null)
				{
					return;
				}
				t.stackCount = Mathf.Min(count, stackLimit);
				count -= t.stackCount;
				if (!pawn.inventory.innerContainer.TryAddOrTransfer(t))
				{
					// Inventory refused the stack (Combat Extended bulk/weight limits, for one) - stop instead of looping.
					if (!t.Destroyed)
					{
						t.Destroy();
					}
					return;
				}
			}
		}

		public override void Notify_UsedWeapon(Pawn pawn)
		{
			// This comp can sit on a weapon def that is also used outside the sub-turret system
			// (a CompProperties_VehicleWeapon default weapon, for instance), where there is no SubTurret to talk to.
			if (turret == null || pawn?.inventory?.innerContainer == null)
			{
				return;
			}
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
			if (turret?.PawnOwner?.inventory?.innerContainer == null)
			{
				return true;
			}
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