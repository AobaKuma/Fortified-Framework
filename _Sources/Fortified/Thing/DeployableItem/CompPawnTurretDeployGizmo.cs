using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using UnityEngine;
using System;

namespace Fortified
{
    internal class CompPawnTurretDeployGizmo : ThingComp
    {
        static List<IntVec3> AcceptedCell(Pawn pawn)
        {
            return new List<IntVec3>() { pawn.Position + IntVec3.South, pawn.Position + IntVec3.North, pawn.Position + IntVec3.East, pawn.Position + IntVec3.West };
        }

        public static TargetingParameters TargetParam(Pawn pawn)
        {
            return new TargetingParameters
            {
                canTargetLocations = true,
                canTargetSelf = false,
                canTargetPawns = false,
                canTargetFires = false,
                canTargetBuildings = false,
                canTargetItems = false,
                validator = (TargetInfo x) => AcceptedCell(pawn).Contains(x.Cell) && x.Cell.GetEdifice(pawn.Map) == null
            };
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Pawn parentpawn = parent as Pawn;
            if (parentpawn == null && Find.Selector.SingleSelectedThing == parentpawn && parentpawn.Drafted)
            {
                foreach (Thing thing in parentpawn.inventory.innerContainer)
                {
                    if (thing is MinifiedThingDeployable deployable)
                    {
                        Command_Target command_Target = new Command_Target
                        {
                            defaultLabel = deployable.InnerThing.Label,
                            targetingParams = TargetParam(parentpawn),
                            icon = deployable.InnerThing.def.GetUIIconForStuff(null),
                            action = delegate (LocalTargetInfo target)
                            {
                                deployable.Deploy(target.Cell, parentpawn);
                            }
                        };
                        yield return command_Target;
                    }
                }
            }
            yield break;
        }
    }

	public interface IGizmoGiver
	{
		Gizmo GetGizmoForPawn(Pawn pawn);
	}

	public class MinifiedThingDeployable : MinifiedThing, IGizmoGiver
	{
        MinifiedThingDeployableGraphicExt ext;

        MinifiedThingDeployableGraphicExt Ext
        {
            get
            {
                ext ??= InnerThing?.def.GetModExtension<MinifiedThingDeployableGraphicExt>();
                return ext;
            }
        }

        public override Graphic Graphic
        {
            get
            {
                if (!Spawned && Ext != null)
                {
                    return Ext.graphicData.Graphic;
                }
                return base.Graphic;
            }
        }

		public Gizmo GetGizmoForPawn(Pawn pawn)
		{
            if(pawn.RaceProps.intelligence < Intelligence.ToolUser)
            {
                return null;
            }
			Command_Target command_Target = new Command_Target
			{
				defaultLabel = this.InnerThing.Label,
				targetingParams = CompPawnTurretDeployGizmo.TargetParam(pawn),
				icon = this.InnerThing.def.GetUIIconForStuff(null),
				action = delegate (LocalTargetInfo target)
				{
					this.Deploy(target.Cell, pawn);
				}
			};
			if (!pawn.Drafted) command_Target.Disable("FFF.DisabledUndrafted".Translate());
			return command_Target;
		}

		public bool Deploy(IntVec3 cell, Pawn workerPawn)
        {
            workerPawn.rotationTracker.Face(cell.ToVector3Shifted());

            foreach (IntVec3 item in GenAdj.OccupiedRect(cell, workerPawn.Rotation, InnerThing.def.size))
            {
                if (item.GetEdifice(workerPawn.Map) != null)
                {
                    Messages.Message("FFF.MinifiedDeployable.SelectedAreaBlocked".Translate(), workerPawn, MessageTypeDefOf.RejectInput, historical: false);
                    return false;
                }
            }

            Thing createdThing = InnerThing;
            Map map = workerPawn.Map;
            GenSpawn.WipeExistingThings(cell, workerPawn.Rotation, createdThing.def, map, DestroyMode.Deconstruct);

            DeployCECompatHook(this, createdThing);

            if (createdThing.def.CanHaveFaction)
            {
                createdThing.SetFactionDirect(workerPawn.Faction);
                createdThing.stackCount = 1;
            }
            Thing thing = GenSpawn.Spawn(createdThing, cell, map, workerPawn.Rotation, WipeMode.VanishOrMoveAside);
            if (thing.TryGetComp<CompMannable>() != null && !workerPawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                Find.Selector.Deselect(workerPawn);
                Find.Selector.Select(thing, playSound: false, forceDesignatorDeselect: false);
                Job job = JobMaker.MakeJob(RimWorld.JobDefOf.ManTurret, thing);
                workerPawn.jobs.TryTakeOrderedJob(job, 0, true);
            }
            if (!Destroyed)
            {
                Destroy();
            }
            return true;
        }
        public static void DeployCECompatHook(MinifiedThingDeployable minified, Thing turret) { }
    }

    public class MinifiedThingDeployableGraphicExt : DefModExtension
    {
        public GraphicData graphicData;
    }
}
