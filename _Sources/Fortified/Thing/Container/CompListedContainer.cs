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
	public class CompProperties_ListedContainer : CompProperties_Interactable
	{
		public ThingSetMakerDef lootMaker;

		public FactionDef lootFaction;

		public CompProperties_ListedContainer()
		{
			compClass = typeof(CompListedContainer);
		}
	}
	public class CompListedContainer : CompInteractable
	{
		public Building_ListedContainer Container => (Building_ListedContainer)parent;

		public new CompProperties_ListedContainer Props => (CompProperties_ListedContainer)props;

		public override void PostPostMake()
		{
			base.PostPostMake();
			ThingSetMakerParams parms = default(ThingSetMakerParams);
			if(Props.lootFaction == null)
			{
				parms.makingFaction = parent.Faction;
			}
			else
			{
				parms.makingFaction = Find.FactionManager.FirstFactionOfDef(Props.lootFaction);
			}
			Container.innerContainer.TryAddRangeOrTransfer(Props.lootMaker.root.Generate(parms));
		}

		public override AcceptanceReport CanInteract(Pawn activateBy = null, bool checkOptionalItems = true)
		{
			if (!Container.locked)
			{
				return false;
			}
			return base.CanInteract(activateBy, checkOptionalItems);
		}

		public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
		{
			if (!Container.locked)
			{
				yield break;
			}
			foreach (FloatMenuOption item in base.CompFloatMenuOptions(selPawn))
			{
				yield return item;
			}
		}

		public override IEnumerable<Gizmo> CompGetGizmosExtra()
		{
			if (!Container.locked)
			{
				yield break;
			}
			foreach (Gizmo item in base.CompGetGizmosExtra())
			{
				yield return item;
			}
		}

		public override string CompInspectStringExtra()
		{
			return null;
		}

		protected override void OnInteracted(Pawn caster)
		{
			Container.locked = false;
		}
	}
}
