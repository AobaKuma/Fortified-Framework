using Multiplayer.API;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Fortified
{
	[StaticConstructorOnStartup]
	public class SubTurret : IAttackTargetSearcher, IExposable
	{
		public bool HasTarget => currentTarget != null;
		public Thing Thing => this.parent;
		public Verb CurrentEffectiveVerb => cachedPrimaryVerb ??= this.GunCompEq.PrimaryVerb;
		public CompEquippable GunCompEq => cachedGunCompEq ??= this.turret.TryGetComp<CompEquippable>();
		public LocalTargetInfo LastAttackedTarget => this.lastAttackedTarget;
		public int LastAttackTargetTick => this.lastAttackTargetTick;
		public int CooldownTimeAdjusted => this.CurrentEffectiveVerb.verbProps.defaultCooldownTime.SecondsToTicks();
		public int WarmupTimeAdjusted => TurretProp.warmingTime.SecondsToTicks();

		private CompTurretProjectile ammo;

		public CompTurretProjectile Ammo => ammo ?? (ammo = turret.TryGetComp<CompTurretProjectile>());

		public Pawn PawnOwner
		{
			get
			{
				if (!(parent is Apparel { Wearer: var wearer }))
				{
					if (parent is Pawn result)
					{
						return result;
					}
					return null;
				}
				return wearer;
			}
		}
		private bool CanShoot(Pawn owner)
		{
			if (owner != null)
			{
				if (cannotShootNoAmmo)
				{
					return false;
				}
				if (!owner.Spawned || owner.Downed || owner.Dead || !owner.Awake()) return false;
				if (owner.stances.stunner.Stunned) return false;
				if (!HasTurret(owner)) return false;
			}
			if (!dormantResolved)
			{
				cachedDormant = this.parent.TryGetComp<CompCanBeDormant>();
				dormantResolved = true;
			}
			return cachedDormant == null || cachedDormant.Awake;
		}

		private bool WarmingUp => burstWarmupTicksLeft > 0;
		public bool HasTurret(Pawn owner)
		{
			return turret != null
				&& owner != null
				&& (CurrentEffectiveVerb.verbProps.linkedBodyPartsGroup == null
				|| !CurrentEffectiveVerb.verbProps.ensureLinkedBodyPartsGroupAlwaysUsable
				|| PawnCapacityUtility.CalculateNaturalPartsAverageEfficiency(owner.health.hediffSet, this.CurrentEffectiveVerb.verbProps.linkedBodyPartsGroup) > 0f);
		}

		public SubTurretProperties TurretProp
		{
			get
			{
				if (turretProp == null)
				{
					parent.TryGetComp<CompMultipleTurretGun>().Init();
				}
				return turretProp;
			}
		}

		public void Init(SubTurretProperties prop)
		{
			this.turretProp = prop;
			if (this.TurretProp.turret != null)
			{
				this.turret ??= ThingMaker.MakeThing(this.TurretProp.turret, null);
			}
			if (turret != null)
			{
				this.UpdateGunVerbs();
			}
		}
		private void UpdateGunVerbs()
		{
			turret.TryGetComp<CompTurretProjectile>()?.InitFromTurret(this);
			List<Verb> allVerbs = this.turret.TryGetComp<CompEquippable>().AllVerbs;
			for (int i = 0; i < allVerbs.Count; i++)
			{
				Verb verb = allVerbs[i];
				verb.caster = this.parent;
				verb.verbProps.warmupTime = 0;
				verb.castCompleteCallback = delegate ()
				{
					this.burstCooldownTicksLeft = CooldownTimeAdjusted;
				};
			}
		}

		public void Tick()
		{
			Pawn owner = PawnOwner;
			if (CanShoot(owner) == false)
			{
				if (parent.IsHashIntervalTick(60))
				{
					cannotShootNoAmmo = false; //Check if ammo arrived
				}
				return;
			}
			if (!fireAtWill && !targetForced)
			{
				if (burstCooldownTicksLeft > 0)
				{
					burstCooldownTicksLeft--;
				}
				return;
			}
			if (CheckTarget())
			{
				curRotation = (this.currentTarget.Cell.ToVector3Shifted() - owner.DrawPos).AngleFlat() + this.TurretProp.angleOffset;
			}
			else
			{
				curRotation = this.TurretProp.angleOffset + this.TurretProp.IdleAngleOffset + owner.Rotation.AsAngle;
			}
			CurrentEffectiveVerb.VerbTick();
			if (CurrentEffectiveVerb.state != VerbState.Bursting)
			{
				if (WarmingUp)
				{
					burstWarmupTicksLeft--;
					if (burstWarmupTicksLeft == 0)
					{
						if (!CurrentEffectiveVerb.TryStartCastOn(currentTarget, currentTarget, false, true, false, true))
						{
							burstWarmupTicksLeft = 1;
						}
						lastAttackTargetTick = Find.TickManager.TicksGame;
						lastAttackedTarget = currentTarget;
						return;
					}
				}
				else
				{
					if (burstCooldownTicksLeft > 0)
					{
						burstCooldownTicksLeft--;
					}
					if (burstCooldownTicksLeft <= 0 && owner.IsHashIntervalTick(10))
					{
						if (Ammo?.OutOfAmmo() == true)
						{
							RunOutOfAmmo();
							return;
						}
						if (TurretProp.autoAttack && !targetForced)
						{
							if (owner.Faction?.IsPlayer != true || owner.Drafted)
							{
								currentTarget = (Thing)AttackTargetFinder.BestShootTargetFromCurrentPosition(this, TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable, null, 0f, 9999f);
							}
						}
						if (currentTarget.IsValid)
						{
							burstWarmupTicksLeft = Mathf.Max(1, WarmupTimeAdjusted);
							return;
						}
						ResetCurrentTarget();
					}
				}
			}
		}

		public void RunOutOfAmmo()
		{
			cannotShootNoAmmo = true;
			burstWarmupTicksLeft = -1;
			CurrentEffectiveVerb.state = VerbState.Idle;
		}

		private bool CheckTarget()
		{
			if (!currentTarget.IsValid)
			{
				return false;
			}
			if (currentTarget.HasThing && (currentTarget.Thing.Map != PawnOwner.Map || (currentTarget.TryGetPawn(out var p) && p.DeadOrDowned)))
			{
				currentTarget = LocalTargetInfo.Invalid;
				targetForced = false;
				return false;
			}
			return true;
		}

		private void ResetCurrentTarget()
		{
			this.currentTarget = LocalTargetInfo.Invalid;
			this.burstWarmupTicksLeft = 0;
			targetForced = false;
			cannotShootNoAmmo = false;
		}
		public PawnRenderNode RenderNode(Pawn pawn)
		{
			TurretProp.renderNodeProperty.overrideMeshSize = Vector2.one;
			PawnRenderNode_SubTurretGun result = (PawnRenderNode_SubTurretGun)Activator.CreateInstance(TurretProp.renderNodeProperty.nodeClass, new object[]
				{
						pawn,
						TurretProp.renderNodeProperty,
						pawn.Drawer.renderer.renderTree
				});
			result.subturret = this;
			return result;
		}

		public void SwitchAutoFire()
		{
			this.fireAtWill = !this.fireAtWill;
		}

		public void SwitchAutoFire(bool value)
		{
			this.fireAtWill = value;
		}

		public void Targetting(List<SubTurret> extraTurrets)
		{
			var tar = Find.Targeter;
			tar.BeginTargeting(this.CurrentEffectiveVerb.targetParams, (t) =>
			{
				[SyncMethod]
				void SyncTarget(LocalTargetInfo t, SubTurret self)
				{
					self.targetForced = true; self.currentTarget = t;
				}
				if (extraTurrets != null)
				{
					foreach (SubTurret turret in extraTurrets)
					{
						SyncTarget(t, turret);
					}
				}
				else SyncTarget(t, this);
			}, delegate (LocalTargetInfo t)
			{
				if (this.CurrentEffectiveVerb.ValidateTarget(t))
				{
					GenDraw.DrawTargetHighlight(t);
				}
				if (extraTurrets.NullOrEmpty())
				{
					this.CurrentEffectiveVerb.verbProps.DrawRadiusRing(this.CurrentEffectiveVerb.caster.Position);
				}
				else
				{
					foreach (SubTurret turret in extraTurrets)
					{
						turret.CurrentEffectiveVerb.verbProps.DrawRadiusRing(turret.CurrentEffectiveVerb.caster.Position);
					}
				}
			}, (x) => extraTurrets.NullOrEmpty() ? this.CurrentEffectiveVerb.ValidateTarget(x, true) : extraTurrets.Any(t => t.CurrentEffectiveVerb.ValidateTarget(x, true)));
		}


		public void ClearTarget()
		{
			[SyncMethod] void SyncClearTarget(SubTurret self)
			{
				self.targetForced = false; self.currentTarget = LocalTargetInfo.Invalid; burstWarmupTicksLeft = 0;
			}
			SyncClearTarget(this);
		}

		public void RemoveWeapon(bool drop = true)
		{
			try
			{
				if (drop)
				{
					if (parent.MapHeld == null)
					{
						ThingOwner parentHolder = parent.ParentHolder?.GetDirectlyHeldThings();
						if (parentHolder != null)
						{
							parentHolder.TryAddOrTransfer(turret);
						}
					}
					else
					{
						GenPlace.TryPlaceThing(turret, parent.PositionHeld, parent.MapHeld, ThingPlaceMode.Near);
					}
				}
			}
			finally
			{
				turret = null;
			}
			ClearCachedValues();
			ClearTarget();
		}

		public void AddWeapon(ThingWithComps weapon)
		{
			if (weapon.Spawned)
			{
				weapon.DeSpawn();
			}
			turret = weapon;
			ClearCachedValues();
			this.UpdateGunVerbs();
			SwitchAutoFire(true);
		}

		public void ClearCachedValues()
		{
			ammo = null;
			cannotShootNoAmmo = false;
			cachedGunCompEq = null;
			cachedPrimaryVerb = null;
			burstCooldownTicksLeft = 0;
			burstWarmupTicksLeft = 0;
			targetForced = false;
			lastAttackedTarget = LocalTargetInfo.Invalid;
			currentTarget = LocalTargetInfo.Invalid;
			lastAttackTargetTick = 0;
			PawnOwner.Drawer.renderer.SetAllGraphicsDirty();
		}
		//

		public void ExposeData()
		{
			Scribe_Values.Look(ref this.ID, "ID");

			//Scribe_References.Look(ref this.parent, "parent"); Obsolete???
			Scribe_Deep.Look(ref this.turret, "turret");


			Scribe_Values.Look<int>(ref this.burstCooldownTicksLeft, "burstCooldownTicksLeft", 0, false);
			Scribe_Values.Look<int>(ref this.burstWarmupTicksLeft, "burstWarmupTicksLeft", 0, false);
			Scribe_Values.Look(ref targetForced, "targetForced");
			Scribe_TargetInfo.Look(ref this.currentTarget, "currentTarget");

			Scribe_Values.Look<bool>(ref this.fireAtWill, "fireAtWill", true, false);
		}

		[NoTranslate]
		public string ID = "null";

		public Thing parent;
		public Thing turret;

		public bool cannotShootNoAmmo = false;

		public int burstCooldownTicksLeft;
		public int burstWarmupTicksLeft;
		public bool targetForced = false;
		public LocalTargetInfo currentTarget = LocalTargetInfo.Invalid;

		public bool fireAtWill = true;
		private LocalTargetInfo lastAttackedTarget = LocalTargetInfo.Invalid;
		private int lastAttackTargetTick;
		private SubTurretProperties turretProp;

		private CompEquippable cachedGunCompEq;
		private Verb cachedPrimaryVerb;
		private CompCanBeDormant cachedDormant;
		private bool dormantResolved;

		public float curRotation;
	}

	public class SubTurretProperties
	{
		[NoTranslate]
		public string ID;
		public ThingDef turret;//Set this not null if want to create mech with turret on spawn or 
		public string supportedWeaponTag; //NullOrEmpty states for cannot equip
		public List<string> generateWithWeapons = new List<string>();
		public float IdleAngleOffset;
		public float angleOffset;
		public bool autoAttack = true;
		public float warmingTime = 1f;
		public PawnRenderNodeProperties renderNodeProperty;
		public List<PawnRenderNodeProperties> renderNodeProperties;
	}
}
