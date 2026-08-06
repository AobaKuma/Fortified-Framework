using RimWorld;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using static System.Net.Mime.MediaTypeNames;

namespace Fortified
{
	public class CompProperties_AccessKeyActivatable : CompProperties
	{
		public int ticksToActivate = 60;

		public ThingDef accessKeyDef;

		public int accessKeyCount = 1;

		public string activatedTexPath = "";

		public ThingSetMakerDef lootMaker = null;

		public FactionDef lootFaction;

		public TargetingParameters targetingParameters = new TargetingParameters()
		{
			canTargetBuildings = false,
			canTargetAnimals = false,
			canTargetMechs = false,
			onlyTargetControlledPawns = true
		};

		public CompProperties_AccessKeyActivatable()
		{
			compClass = typeof(CompAccessKeyActivatable);
		}
	}
	public class CompAccessKeyActivatable : ThingComp, ITargetingSource
	{
		public CompProperties_AccessKeyActivatable Props => (CompProperties_AccessKeyActivatable)props;

		public bool activated = false;

		public float progress;

		public virtual int TicksToActivate => Props.ticksToActivate;

		public virtual bool HideGizmo => activated;

		public bool CasterIsPawn => true;

		public bool IsMeleeAttack => false;

		public bool Targetable => true;

		public bool MultiSelect => false;

		public bool HidePawnTooltips => true;

		public Thing Caster => parent;

		public Pawn CasterPawn => null;

		public Verb GetVerb => null;

		public TargetingParameters targetParams => Props.targetingParameters;

		public virtual ITargetingSource DestinationSelector => null;

		public Texture2D UIIcon => Props.accessKeyDef.uiIcon;

		public virtual AcceptanceReport CanActivate(Pawn activateBy, bool ignoreItems = true)
		{
			if (activateBy.Dead)
			{
				return "PawnIsDead".Translate(activateBy);
			}
			if (parent.PositionHeld.IsForbidden(activateBy) && !activateBy.Drafted)
			{
				return "CannotPrioritizeForbiddenOutsideAllowedArea".Translate() + ": " + activateBy.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap.Label;
			}
			if (parent.IsForbidden(activateBy) && !activateBy.Drafted)
			{
				return "CannotPrioritizeCellForbidden".Translate();
			}
			if (activateBy.Downed)
			{
				return "MessageRitualPawnDowned".Translate(activateBy);
			}
			if (activateBy.Deathresting)
			{
				return "IsDeathresting".Translate(activateBy.Named("PAWN"));
			}
			if (!activateBy.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
			{
				return "MessageIncapableOfManipulation".Translate(activateBy);
			}
			if (!activateBy.CanReach(parent.SpawnedParentOrMe, PathEndMode.ClosestTouch, Danger.Deadly))
			{
				return "CannotReach".Translate();
			}
			if (!ignoreItems && (activateBy.inventory?.innerContainer?.NullOrEmpty() != false || activateBy.inventory.innerContainer.FirstOrDefault(t => t?.def == Props.accessKeyDef && t.stackCount >= Props.accessKeyCount) == null) && !ReservationUtility.ExistsUnreservedAmountOfDef(parent.MapHeld, Props.accessKeyDef, Faction.OfPlayer, Props.accessKeyCount))
			{
				return "MissingMaterials".Translate(Props.accessKeyDef.LabelCap + " x" + Props.accessKeyCount);
			}
			return true;
		}

		public virtual void Activate(Pawn caster, bool force = false)
		{
			activated = true;
			parent.BroadcastCompSignal("FFF_ActivatedByAccessKey");
			if(Props.lootMaker != null)
			{
				ThingSetMakerParams parms = default(ThingSetMakerParams);
				if (Props.lootFaction == null)
				{
					parms.makingFaction = parent.Faction;
				}
				else
				{
					parms.makingFaction = Find.FactionManager.FirstFactionOfDef(Props.lootFaction);
				}
				List<Thing> list = Props.lootMaker.root.Generate(parms);
				if (!list.NullOrEmpty())
				{
					CellRect rect = parent.OccupiedRect();
					IntVec3 cell = parent.def.hasInteractionCell ? parent.InteractionCell : parent.Position;
					foreach (Thing thing in list)
					{
						GenPlace.TryPlaceThing(thing, cell, parent.Map, ThingPlaceMode.Near, extraValidator: c => !rect.Contains(c));
					}
				}
			}
		}

		public override IEnumerable<Gizmo> CompGetGizmosExtra()
		{
			if (HideGizmo)
			{
				yield break;
			}
			if (parent.SpawnedOrAnyParentSpawned)
			{
				Command_Action command_Action = new Command_Action
				{
					defaultLabel = "OrderActivation".Translate() + "...",
					defaultDesc = "OrderActivationDesc".Translate(parent.Named("THING")) + "\n\n" + "Requires".Translate() + ": " + Props.accessKeyDef.LabelCap + " x" + Props.accessKeyCount,
					icon = UIIcon,
					groupable = false,
					action = delegate
					{
						SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
						Find.Targeter.BeginTargeting(this);
					}
				};
				yield return command_Action;
			}
		}

		public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
		{
			if (HideGizmo)
			{
				yield break;
			}
			AcceptanceReport acceptanceReport = CanActivate(selPawn, false);
			FloatMenuOption floatMenuOption = RimWorld.FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption("Activate".Translate(), delegate
			{
				OrderForceTarget(selPawn);
			}), selPawn, parent);
			if (!acceptanceReport.Accepted)
			{
				floatMenuOption.Disabled = true;
				floatMenuOption.Label = floatMenuOption.Label + " (" + acceptanceReport.Reason.UncapitalizeFirst() + ")";
			}
			yield return floatMenuOption;
		}

		public override void PostExposeData()
		{
			Scribe_Values.Look(ref activated, "activated", defaultValue: false);
			Scribe_Values.Look(ref progress, "progress", defaultValue: 0);
		}

		public bool CanHitTarget(LocalTargetInfo target)
		{
			return ValidateTarget(target, showMessages: false);
		}

		public bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
		{
			if (!target.IsValid || target.Pawn == null)
			{
				return false;
			}
			Pawn pawn = target.Pawn;
			AcceptanceReport acceptanceReport = CanActivate(pawn, false);
			if (!acceptanceReport.Accepted)
			{
				if (showMessages && !acceptanceReport.Reason.NullOrEmpty())
				{
					Messages.Message("CannotGenericWorkCustom".Translate("Activate".Translate()) + ": " + acceptanceReport.Reason.CapitalizeFirst(), pawn, MessageTypeDefOf.RejectInput, historical: false);
				}
				return false;
			}
			return true;
		}

		public void DrawHighlight(LocalTargetInfo target)
		{
			if (target.IsValid)
			{
				GenDraw.DrawTargetHighlight(target);
			}
		}

		public void OrderForceTarget(LocalTargetInfo target)
		{
			if (ValidateTarget(target, showMessages: false))
			{
				OrderActivation(target.Pawn);
			}
		}

		public virtual void OrderActivation(Pawn pawn)
		{
			List<Thing> listB = new List<Thing>();
			int count = Props.accessKeyCount;
			Thing targetC = pawn.inventory?.innerContainer?.FirstOrDefault(t => t?.def == Props.accessKeyDef);
			if(targetC != null)
			{
				count -= targetC.stackCount;
			}
			if(count > 0)
			{
				listB = HaulAIUtility.FindFixedIngredientCount(pawn, Props.accessKeyDef, count);
				if(listB == null)
				{
					listB = new List<Thing>();
				}
				count -= listB.Sum(x => x.stackCount);
			}
			if (count <= 0)
			{
				Job job = JobMaker.MakeJob(FFF_DefOf.FFF_UseAccessKey, parent, listB.NullOrEmpty() ? LocalTargetInfo.Invalid : listB[0], targetC ?? LocalTargetInfo.Invalid);
				job.targetQueueB = (from i in listB.Skip(1)
										select new LocalTargetInfo(i)).ToList();
				job.count = Props.accessKeyCount;
				job.playerForced = true;
				pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
			}
		}

		public void OnGUI(LocalTargetInfo target)
		{
			Widgets.MouseAttachedLabel("ChooseWhoShouldActivate".Translate());
			if (ValidateTarget(target, showMessages: false) && Props.targetingParameters.CanTarget(target.Pawn, this))
			{
				GenUI.DrawMouseAttachment(UIIcon);
			}
			else
			{
				GenUI.DrawMouseAttachment(TexCommand.CannotShoot);
			}
		}

		private Graphic graphicInt;

		protected Graphic ActivatedGraphic
		{
			get
			{
				if (graphicInt == null)
				{
					GraphicData graphicData = new GraphicData();
					graphicData.CopyFrom(parent.def.graphicData);
					graphicData.texPath = Props.activatedTexPath;
					graphicInt = graphicData.GraphicColoredFor(parent);
				}
				return graphicInt;
			}
		}

		public override void PostDraw()
		{
			if (activated && parent.def.drawerType != DrawerType.MapMeshOnly && !Props.activatedTexPath.NullOrEmpty())
			{
				ActivatedGraphic.Draw(parent.DrawPos, parent.Rotation, parent);
			}
		}

		public override void PostPrintOnto(SectionLayer layer)
		{
			if (activated && parent.def.drawerType != DrawerType.RealtimeOnly && !Props.activatedTexPath.NullOrEmpty())
			{
				ActivatedGraphic.Print(layer, parent, 0f);
			}
		}

		public override bool DontDrawParent()
		{
			if (activated && !Props.activatedTexPath.NullOrEmpty())
			{
				return true;
			}
			return false;
		}
	}
}
