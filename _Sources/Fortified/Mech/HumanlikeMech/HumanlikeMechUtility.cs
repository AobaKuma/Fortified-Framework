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
			if (pawn == null)
			{
				return;
			}

			if (pawn.story != null)
			{
				try { _ = pawn.story.SkinColorBase; }
				catch (System.InvalidOperationException) { pawn.story.SkinColorBase = Color.white; }
			}

			HumanlikeMechExtension ext = Extension(pawn);
			if (ext == null)
			{
				// 沒有 extension 就不建 tracker，但 workSettings 可能由原版／其他來源建立，
				// 仍要套用白名單，否則一樣會拿到設計外的工作權限。
				ApplyWorkTypeRestrictions(pawn);
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
			}

			// 不論 workSettings 是本次新建、還是讀檔／其他來源帶進來的，都重新套用白名單。
			// 舊存檔中的非玩家機兵帶著超出設計的 priorities，只有每次都套用才能修正。
			ApplyWorkTypeRestrictions(pawn);
		}

		/// <summary>讀檔期間延後處理的 pawn，避免同一次讀檔對同一隻重複排隊。</summary>
		private static readonly HashSet<Pawn> deferredRestrictionPawns = new HashSet<Pawn>();

		/// <summary>
		/// 依 race.mechEnabledWorkTypes 白名單關閉其餘工作類型。
		/// 原版只在 IsColonyMech（玩家陣營、非精神狀態、Biotech 啟用）時才把白名單外的工作列為 disabled，
		/// 所以非玩家機械體／精神狀態中的機兵完全不受限制；這裡不看陣營一律套用。
		/// </summary>
		public static void ApplyWorkTypeRestrictions(Pawn pawn)
		{
			if (pawn?.workSettings == null || !pawn.workSettings.EverWork || pawn.Discarded)
			{
				return;
			}

			RaceProperties race = pawn.RaceProps;
			if (race == null)
			{
				return;
			}

			List<WorkTypeDef> allowed = race.mechEnabledWorkTypes;

			// 白名單為空時機械體一律 fail-closed（與原版對 IsColonyMech 的處理一致：
			// 白名單外全部 disabled，空白名單就是全關），避免 def 解析失敗時變成不限制。
			// 非機械體（人型 AMO）沒有這層原版語意，空白名單視為「未設限」才放行。
			if (allowed.NullOrEmpty() && !race.IsMechanoid)
			{
				return;
			}

			// 讀檔期間 SetPriority(0) 會觸發 jobs.Notify_WorkTypeDisabled → 可能 EndCurrentJob，
			// 此時地圖／保留系統尚未就緒，延到讀檔結束再做。
			if (Scribe.mode != LoadSaveMode.Inactive)
			{
				Pawn deferred = pawn;
				if (deferredRestrictionPawns.Add(deferred))
				{
					LongEventHandler.ExecuteWhenFinished(delegate
					{
						deferredRestrictionPawns.Remove(deferred);
						ApplyWorkTypeRestrictions(deferred);
					});
				}
				return;
			}

			// PostMake can cache work capabilities before generation assigns the faction/story.
			// Refresh capabilities as well as priorities when generation/spawn reapplies restrictions.
			pawn.Notify_DisabledWorkTypesChanged();

			List<WorkTypeDef> all = DefDatabase<WorkTypeDef>.AllDefsListForReading;
			for (int i = 0; i < all.Count; i++)
			{
				WorkTypeDef workType = all[i];
				if (workType == null || (allowed != null && allowed.Contains(workType)))
				{
					continue;
				}
				if (pawn.workSettings.GetPriority(workType) != 0)
				{
					pawn.workSettings.SetPriority(workType, 0);
				}
			}
		}
	}
}
