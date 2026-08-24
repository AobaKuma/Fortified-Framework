using System.Collections.Generic;
using System.Runtime.CompilerServices;

using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified
{
	/// <summary>
	/// 人型機械的共用初始化/渲染邏輯（選項 A）：
	/// HumanlikeMech 與 ArtificialOrganismHumanlike 都透過這裡取得人型支援，
	/// 避免因 C# 單一繼承而重複 code。
	/// </summary>
	public static class HumanlikeMechUtility
	{
		/// <summary>每 Pawn 的頭部圖形快取（無髮型分支）。</summary>
		private static readonly ConditionalWeakTable<Pawn, Graphic> headGraphicCache = new ConditionalWeakTable<Pawn, Graphic>();

		public static HumanlikeMechExtension Extension(Pawn pawn) => pawn.def.GetModExtension<HumanlikeMechExtension>();

		/// <summary>計算頭部 Graphic（含髮型切換），對應原 HumanlikeMech.HeadGraphic。</summary>
		public static Graphic GetHeadGraphic(Pawn pawn)
		{
			HumanlikeMechExtension ext = Extension(pawn);
			if (ext == null || ext.headGraphic == null)
			{
				return null;
			}
			if (ext.canChangeHairStyle && HasHair(pawn))
			{
				return ext.headGraphicHaired?.Graphic ?? ext.headGraphic.Graphic;
			}
			return headGraphicCache.GetValue(pawn, p => ext.headGraphic.Graphic);
		}

		private static bool HasHair(Pawn pawn) => pawn.story?.hairDef != null && pawn.story.hairDef != HairDefOf.Bald;

		/// <summary>初始化人型機械的 story/style/skills/workSettings，對應原 HumanlikeMech.CheckTracker。</summary>
		public static void CheckTracker(Pawn pawn)
		{
			if (pawn.story != null)
			{
				try { _ = pawn.story.SkinColorBase; }
				catch (System.InvalidOperationException) { pawn.story.SkinColorBase = Color.white; }
			}

			HumanlikeMechExtension ext = Extension(pawn);
			if (ext == null)
			{
				return;
			}

			// 檢查 story 是否是首次初始化
			bool isStoryFirstInit = pawn.story == null;

			pawn.outfits ??= new Pawn_OutfitTracker(pawn);
			pawn.story ??= new Pawn_StoryTracker(pawn);

			// 僅在首次初始化時設置這些值，避免覆蓋加載的數據
			if (isStoryFirstInit)
			{
				pawn.story.bodyType ??= ext.bodyTypeOverride;
				pawn.story.headType ??= ext.headTypeOverride;
				pawn.story.SkinColorBase = Color.white;
				pawn.story.HairColor = Color.white;

				// 如果不允許改變髮型，強制設置為禿頭；否則僅在未初始化時設置
				if (!ext.canChangeHairStyle || pawn.story.hairDef == null)
				{
					pawn.story.hairDef = HairDefOf.Bald;
				}
			}

			pawn.style ??= new Pawn_StyleTracker(pawn)
			{
				beardDef = BeardDefOf.NoBeard,
				FaceTattoo = null,
				BodyTattoo = null,
			};

			pawn.interactions ??= new Pawn_InteractionsTracker(pawn);
			if (pawn.skills == null)
			{
				pawn.skills = new Pawn_SkillTracker(pawn);
				pawn.skills.skills.ForEach(s => s.Level = pawn.def.race.mechFixedSkillLevel);
				if (!ext.skills.NullOrEmpty())
				{
					foreach (SkillRange item in ext.skills)
					{
						pawn.skills.GetSkill(item.Skill).Level = item.Range.RandomInRange;
					}
				}
			}

			// 初始化工作設置，讓機械體能夠被分配工作
			if (pawn.workSettings == null)
			{
				pawn.workSettings = new Pawn_WorkSettings(pawn);
				pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
				ApplyWorkTypeRestrictions(pawn);
			}
		}

		/// <summary>限制機械體工作類型，對應原 HumanlikeMech.ApplyWorkTypeRestrictions。</summary>
		public static void ApplyWorkTypeRestrictions(Pawn pawn)
		{
			if (pawn.workSettings == null) return;
			if (pawn.RaceProps.IsMechanoid || pawn.RaceProps.mechEnabledWorkTypes.NullOrEmpty()) return;
			foreach (WorkTypeDef w in DefDatabase<WorkTypeDef>.AllDefsListForReading)
			{
				if (!pawn.RaceProps.mechEnabledWorkTypes.Contains(w))
				{
					pawn.workSettings.SetPriority(w, 0);
				}
			}
		}
	}
}
