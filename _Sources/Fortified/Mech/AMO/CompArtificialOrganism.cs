using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified
{
	public class CompProperties_ArtificialOrganism : CompProperties
	{
		/// <summary>自體再生間隔（tick）。</summary>
		public int regenIntervalTicks = 120;

		/// <summary>每次再生移除的傷勢 severity。</summary>
		public float regenHealAmount = 1f;

		/// <summary>能量低於此比例時自動消耗口糧充能。</summary>
		public float consumeBelowLevel = 0.3f;

		/// <summary>每份口糧補回的能量（佔 MaxLevel 的比例）。</summary>
		public float energyPerRation = 0.5f;

		/// <summary>每次自動充能消耗的口糧份數。</summary>
		public int rationsPerConsume = 1;

		public CompProperties_ArtificialOrganism()
		{
			compClass = typeof(CompArtificialOrganism);
		}
	}

	/// <summary>
	/// AMO 專屬維生循環：
	/// 1) 自體再生——隨時間移除現存部位的傷勢（缺失部位不處理）。
	/// 2) 口糧→能量——能量低於閾值且口糧艙（IRationSource）有存糧時自動消耗充能。
	/// </summary>
	public class CompArtificialOrganism : ThingComp
	{
		public CompProperties_ArtificialOrganism Props => (CompProperties_ArtificialOrganism)props;

		private Pawn Pawn => parent as Pawn;

		private int ticksToNextRegen;

		private IRationSource cachedRationSource;

		/// <summary>找尋 pawn 身上的口糧來源（IRationSource），快取於 PostSpawnSetup。</summary>
		public IRationSource RationSource
		{
			get
			{
				if (cachedRationSource == null && parent.AllComps != null)
				{
					foreach (ThingComp comp in parent.AllComps)
					{
						if (comp is IRationSource source)
						{
							cachedRationSource = source;
							break;
						}
					}
				}
				return cachedRationSource;
			}
		}

		public override void PostSpawnSetup(bool respawningAfterLoad)
		{
			base.PostSpawnSetup(respawningAfterLoad);
			cachedRationSource = null; // 由 RationSource getter 重新掃描（comp 集合已固定）
			ticksToNextRegen = Props.regenIntervalTicks;
		}

		public override void CompTick()
		{
			base.CompTick();
			Pawn pawn = Pawn;
			if (pawn == null || !pawn.Spawned || pawn.DeadOrDowned) return;

			if (!parent.IsHashIntervalTick(60)) return; // 每秒一次粗粒度檢查，減輕 tick 負擔

			TryRegenerate(pawn);
			TryConsumeRationForEnergy(pawn);
		}

		/// <summary>自體再生：隨機移除現存部位的非永久傷勢。</summary>
		private void TryRegenerate(Pawn pawn)
		{
			ticksToNextRegen -= 60;
			if (ticksToNextRegen > 0) return;
			ticksToNextRegen = Props.regenIntervalTicks;

			if (pawn.health?.hediffSet == null) return;
			List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
			List<Hediff_Injury> candidates = null;
			for (int i = 0; i < hediffs.Count; i++)
			{
				//Hediff_MissingPart 不是 Hediff_Injury，所以缺失部位天然被排除在外。
				if (hediffs[i] is Hediff_Injury injury && !injury.IsPermanent())
				{
					candidates ??= new List<Hediff_Injury>();
					candidates.Add(injury);
				}
			}
			if (candidates == null) return;
			Hediff_Injury chosen = candidates.RandomElement();
			chosen.Severity -= Props.regenHealAmount;
			if (chosen.Severity <= 0f)
			{
				pawn.health.RemoveHediff(chosen);
			}
		}

		/// <summary>能量低於閾值時，自動消耗口糧充能。</summary>
		private void TryConsumeRationForEnergy(Pawn pawn)
		{
			Need_MechEnergy energy = pawn.needs?.energy;
			if (energy == null) return;
			if (energy.CurLevel >= Props.consumeBelowLevel * energy.MaxLevel) return;

			IRationSource source = RationSource;
			if (source == null || source.LoadedRations < Props.rationsPerConsume) return;

			if (source.TryConsume(Props.rationsPerConsume))
			{
				energy.CurLevel = Mathf.Min(energy.MaxLevel, energy.CurLevel + Props.energyPerRation * energy.MaxLevel);
			}
		}

		public override string CompInspectStringExtra()
		{
			Pawn pawn = Pawn;
			IRationSource source = RationSource;
			if (pawn == null || source == null || source.RationDef == null)
			{
				return null;
			}
			return "FFF.ArtificialOrganismRations".Translate(
				source.RationDef.LabelCap.Named("RATION"),
				source.LoadedRations.Named("COUNT"),
				source.MaxRations.Named("MAX"));
		}

		public override void PostExposeData()
		{
			base.PostExposeData();
			Scribe_Values.Look(ref ticksToNextRegen, "ticksToNextRegen", 0);
		}
	}
}
