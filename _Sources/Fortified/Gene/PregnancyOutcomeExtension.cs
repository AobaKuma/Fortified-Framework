using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Fortified
{
	[HarmonyPatch(typeof(PregnancyUtility), nameof(PregnancyUtility.ApplyBirthOutcome))]
	public class Patch_PregnancyUtility_ApplyBirthOutcome
	{

		[HarmonyPrefix]
		public static bool Prefix(RitualOutcomePossibility outcome, float quality, Precept_Ritual ritual, List<GeneDef> genes, Pawn geneticMother, Thing birtherThing, Pawn father, Pawn doctor, LordJob_Ritual lordJobRitual, RitualRoleAssignments assignments, bool preventLetter, ref Thing __result)
		{
			string desc = "";
			Thing result = null;
			if(birtherThing == null)
			{
				return true;
			}
			if(geneticMother?.genes != null)
			{
				foreach (Gene gene in geneticMother.genes.GenesListForReading)
				{
					PregnancyOutcomeExtension ext = gene.def.GetModExtension<PregnancyOutcomeExtension>();
					if (ext != null && ext.TryApply(geneticMother, birtherThing, father, out var d, out result, true))
					{
						if (!desc.NullOrEmpty())
						{
							desc += "\n\n";
						}
						desc += d;
						break;
					}
				}
			}
			if (result == null && !genes.NullOrEmpty())
			{
				foreach (GeneDef def in genes)
				{
					PregnancyOutcomeExtension ext = def.GetModExtension<PregnancyOutcomeExtension>();
					if (ext != null && ext.TryApply(geneticMother, birtherThing, father, out var d, out result))
					{
						if (!desc.NullOrEmpty())
						{
							desc += "\n\n";
						}
						desc += d;
						break;
					}
				}
			}
			if (result != null)
			{
				if (!preventLetter)
				{
					Pawn birtherPawn = birtherThing as Pawn;
					string label = (birtherPawn != null) ? "OutcomeLetterLabel".Translate(result.LabelCapNoCount.Named("OUTCOMELABEL"), ritual.Label.Named("RITUALLABEL")) : "LetterVatBirth".Translate(outcome.label);
					Find.LetterStack.ReceiveLetter(label, desc, LetterDefOf.PositiveEvent, result);
				}
				__result = null;
				return false;
			}
			return true;
		}
	}

	public class PregnancyOutcomeExtension : DefModExtension, IHiddenGeneSource
	{
		public bool applyIfNotGeneticMother = false;

		public bool applyIfVat = true;

		public bool applyIfFromMotherGenes = true;

		public ThingDef thingDef;

		public IntRange stackCount = IntRange.One;

		public ThingDef filthDef;

		public IntRange filthCount = IntRange.One;

		[MustTranslate]
		public string letterText = "";

		// 導向發育基因不是玩家該在異種人編輯器裡挑的東西：它會把嬰兒換成物品。
		// 預設藏起來，個別 Def 想露出來再把這格設成 false。
		public bool hideInGeneEditor = true;

		public bool HideInGeneEditor => hideInGeneEditor;

		public bool TryApply(Pawn geneticMother, Thing birtherThing, Pawn father, out string letterDesc, out Thing result, bool fromMother = false)
		{
			letterDesc = "";
			result = null;
			if(fromMother && !applyIfFromMotherGenes)
			{
				return false;
			}
			Pawn birtherPawn = birtherThing as Pawn;
			if (birtherPawn == null)
			{
				if (!applyIfVat)
				{
					return false;
				}
			}
			else if (birtherPawn != geneticMother && !applyIfNotGeneticMother)
			{
				return false;
			}
			if(thingDef == null)
			{
				return false;
			}
			IntVec3? intVec = null;
			if (birtherThing.Map != null)
			{
				intVec = birtherThing.def.hasInteractionCell ? birtherThing.InteractionCell : birtherThing.Position;
				if(filthDef != null)
				{
					FilthMaker.TryMakeFilth(intVec.Value, birtherThing.Map, filthDef, filthCount.RandomInRange);
				}
			}
			result = ThingMaker.MakeThing(thingDef, null);
			result.stackCount = Mathf.Max(1, stackCount.RandomInRange);
			if (TrySpawnOutcomeItem(result, birtherThing, intVec))
			{
				letterDesc = letterText.Formatted((birtherPawn ?? geneticMother).Named("PAWN"), result.Named("ITEM"));
				return true;
			}
			return false;
		}

		public static bool TrySpawnOutcomeItem(Thing item, Thing birther, IntVec3? positionOverride = null)
		{
			if (birther.SpawnedOrAnyParentSpawned)
			{
				return GenPlace.TryPlaceThing(item, positionOverride ?? birther.PositionHeld, birther.MapHeld, ThingPlaceMode.Near);
			}
			if (birther is Pawn pawn)
			{
				if (pawn.IsCaravanMember())
				{
					pawn.GetCaravan().AddPawnOrItem(item, addCarriedPawnToWorldPawnsIfAny: true);
					TryPassToWorld(item);
					return true;
				}
				if (pawn.IsWorldPawn())
				{
					TryPassToWorld(item);
					return true;
				}
			}
			else if (birther.ParentHolder != null && birther.ParentHolder is Pawn_InventoryTracker pawn_InventoryTracker)
			{
				if (pawn_InventoryTracker.pawn.IsCaravanMember())
				{
					pawn_InventoryTracker.pawn.GetCaravan().AddPawnOrItem(item, addCarriedPawnToWorldPawnsIfAny: true);
					TryPassToWorld(item);
					return true;
				}
				if (pawn_InventoryTracker.pawn.IsWorldPawn())
				{
					TryPassToWorld(item);
					return true;
				}
			}
			TryPassToWorld(item);
			return false;
			void TryPassToWorld(Thing thing)
			{
				if (thing is Pawn p)
				{
					Find.WorldPawns.PassToWorld(p);
				}
			}
		}
	}

}
