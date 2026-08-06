using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Fortified
{
	public interface IStructureLord
	{
		IntVec3 Position { get; }
	}

	public class LordJob_DefendCell : LordJob, IStructureLord
	{
		public IntVec3 position;

		public string attackSignal = "";

		public float sendSignalRadius = -1;

		public int ticksTillFallback = 2500;

		public int ticksTillBackToWork = 5000;

		public IntVec3 Position => position;

		public LordJob_DefendCell()
		{
		}

		public LordJob_DefendCell(IntVec3 position, string attackSignal = "", int ticksTillFallback = 2500, int ticksTillBackToWork = 5000)
		{
			this.position = position;
			this.attackSignal = attackSignal;
			this.ticksTillFallback = ticksTillFallback;
			this.ticksTillBackToWork = ticksTillBackToWork;
		}

		public override StateGraph CreateGraph()
		{
			StateGraph stateGraph = new StateGraph();
			LordToil_DefendPoint lordToil_Stage = (LordToil_DefendPoint)(stateGraph.StartingToil = new LordToil_DefendPoint(position));
			LordToil_AssaultColony lordToil_AssaultColony = new LordToil_AssaultColony();
			stateGraph.AddToil(lordToil_AssaultColony);

			Transition transition1 = new Transition(lordToil_Stage, lordToil_AssaultColony);
			transition1.AddTrigger(new Trigger_PawnHarmed(1f, requireInstigatorWithFaction: false));
			transition1.AddTrigger(new Trigger_Custom((TriggerSignal signal) => ((signal.type == TriggerSignalType.BuildingDamaged) && signal.thing is Building_Turret b && b.Position.DistanceTo(position) <= sendSignalRadius)));
			transition1.AddPostAction(new TransitionAction_Custom(delegate (Transition t)
			{
				foreach (Lord lord in t.Map.lordManager.lords)
				{
					if (lord.faction == this.lord.faction && lord.LordJob is IStructureLord job && (sendSignalRadius < 0 || job.Position.DistanceTo(position) <= sendSignalRadius))
					{
						lord.Notify_SignalReceived(new Signal(attackSignal));
					}
				}
			}));
			stateGraph.AddTransition(transition1);

			Transition transition2 = new Transition(lordToil_Stage, lordToil_AssaultColony);
			transition2.AddTrigger(new Trigger_Custom((TriggerSignal signal) => !attackSignal.NullOrEmpty() && signal.signal.tag == attackSignal));
			stateGraph.AddTransition(transition2);

			Transition transition3 = new Transition(lordToil_AssaultColony, lordToil_Stage);
			transition3.AddTrigger(new Trigger_TicksPassedWithoutHarm(ticksTillFallback));
			stateGraph.AddTransition(transition3);

			return stateGraph;
		}

		public override void ExposeData()
		{
			Scribe_Values.Look(ref position, "position");
			Scribe_Values.Look(ref sendSignalRadius, "sendSignalRadius");
			Scribe_Values.Look(ref ticksTillFallback, "ticksTillFallback");
			Scribe_Values.Look(ref ticksTillBackToWork, "ticksTillBackToWork");
			Scribe_Values.Look(ref attackSignal, "attackSignal");
		}
	}

	public class LordToil_DefendRoom : LordToil
	{
		public override IntVec3 FlagLoc => Data.stagingPoint;

		private LordToilData_Stage Data => (LordToilData_Stage)data;

		public override bool ForceHighStoryDanger => true;

		public LordToil_DefendRoom(IntVec3 stagingLoc)
		{
			data = new LordToilData_Stage();
			Data.stagingPoint = stagingLoc;
		}

		public override void UpdateAllDuties()
		{
			LordToilData_Stage lordToilData_Stage = Data;
			for (int i = 0; i < lord.ownedPawns.Count; i++)
			{
				lord.ownedPawns[i].mindState.duty = new PawnDuty(FFF_DefOf.FFF_DefendRoom, lordToilData_Stage.stagingPoint);
				lord.ownedPawns[i].mindState.duty.radius = 28f;
			}
		}
	}

	public class LordJob_DefendRoom : LordJob, IStructureLord
	{
		public IntVec3 position;

		public string attackSignal = "";

		public float sendSignalRadius = -1;

		public int ticksTillFallback = 2500;

		public int ticksTillBackToWork = 5000;

		public IntVec3 Position => position;

		public LordJob_DefendRoom()
		{
		}

		public LordJob_DefendRoom(IntVec3 position, string attackSignal = "", int ticksTillFallback = 2500, int ticksTillBackToWork = 5000)
		{
			this.position = position;
			this.attackSignal = attackSignal;
			this.ticksTillFallback = ticksTillFallback;
			this.ticksTillBackToWork = ticksTillBackToWork;
		}

		public override StateGraph CreateGraph()
		{
			StateGraph stateGraph = new StateGraph();
			LordToil_DefendRoom lordToil_Stage = (LordToil_DefendRoom)(stateGraph.StartingToil = new LordToil_DefendRoom(position));
			LordToil_AssaultColony lordToil_AssaultColony = new LordToil_AssaultColony();
			stateGraph.AddToil(lordToil_AssaultColony);

			Transition transition1 = new Transition(lordToil_Stage, lordToil_AssaultColony);
			transition1.AddTrigger(new Trigger_PawnHarmed(1f, requireInstigatorWithFaction: false));
			transition1.AddTrigger(new Trigger_Custom((TriggerSignal signal) => ((signal.type == TriggerSignalType.BuildingDamaged) && signal.thing is Building_Turret b && b.Position.DistanceTo(position) <= sendSignalRadius)));
			transition1.AddPostAction(new TransitionAction_Custom(delegate (Transition t)
			{
				foreach (Lord lord in t.Map.lordManager.lords)
				{
					if (lord.faction == this.lord.faction && lord.LordJob is IStructureLord job && (sendSignalRadius < 0 || job.Position.DistanceTo(position) <= sendSignalRadius))
					{
						lord.Notify_SignalReceived(new Signal(attackSignal));
					}
				}
			}));
			stateGraph.AddTransition(transition1);

			Transition transition2 = new Transition(lordToil_Stage, lordToil_AssaultColony);
			transition2.AddTrigger(new Trigger_Custom((TriggerSignal signal) => !attackSignal.NullOrEmpty() && signal.signal.tag == attackSignal));
			stateGraph.AddTransition(transition2);

			Transition transition3 = new Transition(lordToil_AssaultColony, lordToil_Stage);
			transition3.AddTrigger(new Trigger_TicksPassedWithoutHarm(ticksTillFallback));
			stateGraph.AddTransition(transition3);

			return stateGraph;
		}

		public override void ExposeData()
		{
			Scribe_Values.Look(ref position, "position");
			Scribe_Values.Look(ref sendSignalRadius, "sendSignalRadius");
			Scribe_Values.Look(ref ticksTillFallback, "ticksTillFallback");
			Scribe_Values.Look(ref ticksTillBackToWork, "ticksTillBackToWork");
			Scribe_Values.Look(ref attackSignal, "attackSignal");
		}
	}
}
