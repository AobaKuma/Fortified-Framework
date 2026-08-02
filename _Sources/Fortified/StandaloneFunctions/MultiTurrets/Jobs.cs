using Multiplayer.API;
using RimWorld;
using RimWorld.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Noise;
using Verse.Sound;
using static UnityEngine.GraphicsBuffer;

namespace Fortified
{
	public class JobDriver_EquipTurret : JobDriver
	{
		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			return pawn.Reserve(job.targetA, job, 1, 1, null, errorOnFailed);
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			this.FailOnDestroyedOrNull(TargetIndex.A);
			Toil f = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
			if (job.ignoreForbidden)
			{
				yield return f.FailOnDespawnedOrNull(TargetIndex.A);
			}
			else
			{
				yield return f.FailOnDespawnedNullOrForbidden(TargetIndex.A);
			}
			Toil toil = ToilMaker.MakeToil("MakeNewToils");
			toil.initAction = delegate
			{
				CompMultipleTurretGun comp = toil.actor.GetComp<CompMultipleTurretGun>();
				if(comp == null || comp.turrets.Count < job.count)
				{
					return;
				}
				if(comp.turrets[job.count - 1].HasTurret(pawn))
				{
					comp.turrets[job.count - 1].RemoveWeapon(true);
				}
				if(job.GetTarget(TargetIndex.A).Thing is ThingWithComps thing)
				{
					comp.turrets[job.count - 1].AddWeapon(thing);
					thing.def.soundInteract?.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
				}
			};
			toil.defaultCompleteMode = ToilCompleteMode.Instant;
			yield return toil;
		}
	}

	public class JobGiver_PickUpTurretAmmo : ThinkNode_JobGiver
	{
		protected override Job TryGiveJob(Pawn pawn)
		{
			if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
			{
				return null;
			}
			CompMultipleTurretGun comp = pawn.GetComp<CompMultipleTurretGun>();
			if (comp == null)
			{
				return null;
			}
			ThingDefCount result = GetAmmo(pawn, comp);
			if(result.ThingDef == null)
			{
				return null;
			}
			ThingCount thing = GetAmmoThing(pawn, result);
			if (thing.Thing == null)
			{
				return null;
			}
			Job job = JobMaker.MakeJob(JobDefOf.TakeCountToInventory, thing.Thing);
			job.count = result.Count;
			return job;
		}

		private ThingDefCount GetAmmo(Pawn pawn, CompMultipleTurretGun comp)
		{
			foreach(SubTurret item in comp.turrets)
			{
				if(item.Ammo == null)
				{
					continue;
				}
				foreach(var item2 in item.Ammo.ammoSettings)
				{
					if(item2.Value <= 0)
					{
						continue;
					}
					int count = pawn.inventory.Count(item2.Key);
					if (count >= item2.Value)
					{
						continue;
					}
					return new ThingDefCount(item2.Key, item2.Value - count);
				}
			}
			return default(ThingDefCount);
		}

		private ThingCount GetAmmoThing(Pawn pawn, ThingDefCount defCount)
		{
			Thing t = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(defCount.ThingDef), PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f, Validator);
			if(t == null)
			{
				return default(ThingCount);
			}
			return new ThingCount(t, Mathf.Min(t.stackCount, defCount.Count));
			bool Validator(Thing x)
			{
				if (x.IsForbidden(pawn) || !pawn.CanReserve(x))
				{
					return false;
				}
				return true;
			}
		}
	}
}
