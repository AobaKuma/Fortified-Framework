using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified
{
	/// <summary>
	/// 人型 AMO（如 DMS 蛙人）：在 ArtificialOrganism 之上加入
	/// HumanlikeMech 的人型初始化邏輯（story/style/skills/渲染/服裝），
	/// 透過共用 helper（HumanlikeMechUtility）取得，不重複 code。
	/// </summary>
	public class ArtificialOrganismHumanlike : ArtificialOrganism, IHumanlikeMech
	{
		public HumanlikeMechExtension Extension => HumanlikeMechUtility.Extension(this);

		public Graphic HeadGraphic => HumanlikeMechUtility.GetHeadGraphic(this);

		public override void PostMake()
		{
			base.PostMake();
			HumanlikeMechUtility.CheckTracker(this);
		}

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			HumanlikeMechUtility.CheckTracker(this);
		}

		public override void Kill(DamageInfo? dinfo, Hediff exactCulprit = null)
		{
			if (dinfo == null)//解體殺
			{
				var hediffs = health.hediffSet.hediffs.FindAll(h => h.def.spawnThingOnRemoved != null);
				foreach (Hediff item in hediffs)
				{
					health.RemoveHediff(item);
					Thing thing = ThingMaker.MakeThing(item.def.spawnThingOnRemoved);
					thing.stackCount = 1;
					GenPlace.TryPlaceThing(thing, Position, Map, ThingPlaceMode.Near);
				}
			}
			base.Kill(dinfo, exactCulprit);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				HumanlikeMechUtility.CheckTracker(this);
				Drawer?.renderer?.SetAllGraphicsDirty();
			}
		}

		// 限制机械体工作类型（對應 HumanlikeMech 的 public API）
		public void ApplyWorkTypeRestrictions() => HumanlikeMechUtility.ApplyWorkTypeRestrictions(this);
	}
}
