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
	public class JobGiver_RepairMechs_Overseer : ThinkNode_JobGiver
	{
		protected override Job TryGiveJob(Pawn pawn)
		{
			if (!(pawn is IOverseer mech) || !mech.Comp.Props.canRepair)
			{
				return null;
			}
			//AMO 不可被修理（自行再生）。
			if (pawn is ArtificialOrganism amo && !amo.Repairable)
			{
				return null;
			}
			if (MechRepairUtility.CanRepair(pawn) && pawn.GetComp<CompMechRepairable>()?.autoRepair == true)
			{
				return JobMaker.MakeJob(FFF_DefOf.FFF_RepairMech_Overseer, pawn);
			}
			Thing thing = GenClosest.ClosestThing_Global_Reachable(pawn.Position, pawn.Map, pawn.Map.mapPawns.SpawnedPawnsInFaction(pawn.Faction), PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f, (Thing x) => CanRepair(pawn, x));
			if (thing == null)
			{
				return null;
			}
			return JobMaker.MakeJob(FFF_DefOf.FFF_RepairMech_Overseer, thing);
		}

		private static bool CanRepair(Pawn pawn, Thing thing)
		{
			Pawn target = (Pawn)thing;
			if (!target.RaceProps.IsMechanoid)
			{
				return false;
			}
			//AMO 不可被修理（自行再生）。
			if (target is ArtificialOrganism amo && !amo.Repairable)
			{
				return false;
			}
			if (target.Drafted)
			{
				return false;
			}
			//快取 comps（spawn 後有效）；非框架機械保留 TryGetComp 語意。
			CompMechRepairable compMechRepairable = target is ICachedMechComps cc ? cc.MechRepairableComp : target.TryGetComp<CompMechRepairable>();
			if (compMechRepairable == null)
			{
				return false;
			}
			if (target.InAggroMentalState || target.HostileTo(pawn))
			{
				return false;
			}
			if (thing.IsForbidden(pawn))
			{
				return false;
			}
			if (!pawn.CanReserve(target, 1, -1, null, false))
			{
				return false;
			}
			if (target.IsBurning())
			{
				return false;
			}
			if (target.IsAttacking())
			{
				return false;
			}
			if (target.needs.energy == null)
			{
				return false;
			}
			if (!MechRepairUtility.CanRepair(target))
			{
				return false;
			}
			return compMechRepairable.autoRepair;
		}
	}

	public class JobDriver_RepairMech_Overseer : JobDriver
	{
		private const TargetIndex MechInd = TargetIndex.A;

		protected int ticksToNextRepair;

		protected Pawn Mech => (Pawn)job.GetTarget(TargetIndex.A).Thing;

		private IOverseer Overseer => pawn as IOverseer;

		protected int TicksPerHeal => Mathf.RoundToInt(Overseer.Comp.Props.ticksPerHeal);

		private bool selfRepair;

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			return pawn == Mech || pawn.Reserve(Mech, job, 1, -1, null, errorOnFailed);
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			if (pawn == Mech)
			{
				selfRepair = true;
			}
			this.FailOnDestroyedOrNull(TargetIndex.A);
			this.FailOnForbidden(TargetIndex.A);
			this.FailOn(() => Mech.IsAttacking());
			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
			Toil toil = selfRepair ? Toils_General.Wait(int.MaxValue, TargetIndex.None) : Toils_General.WaitWith(TargetIndex.A, int.MaxValue, useProgressBar: false, maintainPosture: true, maintainSleep: true);
			toil.WithEffect(EffecterDefOf.MechRepairing, TargetIndex.A);
			toil.PlaySustainerOrSound(SoundDefOf.RepairMech_Touch);
			toil.AddPreInitAction(delegate
			{
				ticksToNextRepair = TicksPerHeal;
			});
			toil.handlingFacing = true;
			toil.tickIntervalAction = delegate (int delta)
			{
				ticksToNextRepair -= delta;
				if (ticksToNextRepair <= 0)
				{
					Mech.needs.energy.CurLevel -= Mech.GetStatValue(StatDefOf.MechEnergyLossPerHP) * (float)delta;
					MechRepairUtility.RepairTick(Mech);
					ticksToNextRepair = TicksPerHeal;
				}
				if (!selfRepair)
				{
					pawn.rotationTracker.FaceTarget(Mech);
				}
			};
			toil.AddFinishAction(delegate
			{
				if (!selfRepair && Mech.jobs?.curJob != null)
				{
					Mech.jobs.EndCurrentJob(JobCondition.InterruptForced);
				}
			});
			toil.AddEndCondition(() => MechRepairUtility.CanRepair(Mech) ? JobCondition.Ongoing : JobCondition.Succeeded);
			yield return toil;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref ticksToNextRepair, "ticksToNextRepair", 0);
			Scribe_Values.Look(ref selfRepair, "selfRepair");
		}
	}

	public class JobDriver_ControlMech_Overseer : JobDriver
	{
		private const TargetIndex MechInd = TargetIndex.A;

		private Pawn Mech => (Pawn)job.GetTarget(TargetIndex.A).Thing;

		private IOverseer Overseer => pawn as IOverseer;

		private int MechControlTime => Mathf.RoundToInt(Mech.GetStatValue(StatDefOf.ControlTakingTime) * 60f);

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			return pawn.Reserve(Mech, job, 1, -1, null, errorOnFailed);
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
			this.FailOn(() => Overseer == null || !MechanitorUtility.CanControlMech(Overseer.Comp.dummyPawn, Mech));
			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
			yield return Toils_General.WaitWith(TargetIndex.A, MechControlTime, useProgressBar: true, maintainPosture: true, maintainSleep: false, TargetIndex.A).WithEffect(EffecterDefOf.ControlMech, TargetIndex.A);
			Toil toil = ToilMaker.MakeToil("MakeNewToils");
			toil.initAction = delegate
			{
				Overseer.Comp.Connect(Mech);
			};
			toil.PlaySoundAtEnd(SoundDefOf.ControlMech_Complete);
			yield return toil;
		}
	}
}
