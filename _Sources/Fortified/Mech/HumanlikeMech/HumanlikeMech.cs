using System.Collections.Generic;

using Verse;
using RimWorld;
using UnityEngine;
using Verse.AI;
using System.Linq;

namespace Fortified
{
    public class HumanlikeMech : Pawn, IWeaponUsable, ICaravanOwner
    {
		public virtual bool CanCaravan => false;//Maybe should be true?????

		public MechWeaponExtension MechWeapon => def.GetModExtension<MechWeaponExtension>();
        public HumanlikeMechExtension Extension => def.GetModExtension<HumanlikeMechExtension>();
        private Graphic headGraphic;
        public Graphic HeadGraphic
        {
            get
            {
                headGraphic ??= Extension.headGraphic.Graphic;
                if (Extension.canChangeHairStyle && HasHair) headGraphic = Extension.headGraphicHaired.Graphic;
                return headGraphic;
            }
        }
        private bool HasHair => story.hairDef != null && story.hairDef != HairDefOf.Bald;
        public override void PostMake()
        {
            base.PostMake();
            CheckTracker();
        }
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            CheckTracker();
        }
        public override void Kill(DamageInfo? dinfo, Hediff exactCulprit = null)
        {
            if (dinfo == null)//解體殺
            {
                List<Hediff> hediffs = health.hediffSet.hediffs.Where(h => h.def.spawnThingOnRemoved != null).ToList();
                foreach (Hediff item in hediffs)
                {
                    health.RemoveHediff(item);
                    Thing thing = ThingMaker.MakeThing(item.def.spawnThingOnRemoved);
                    thing.stackCount = 1;
                    GenPlace.TryPlaceThing(thing, this.Position, this.Map, ThingPlaceMode.Near);
                }
            }
            base.Kill(dinfo, exactCulprit);
        }
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                CheckTracker();
                this.Drawer?.renderer?.SetAllGraphicsDirty();
            }
        }
        private void CheckTracker()
        {
            if (story != null)
            {
                try { _ = story.SkinColorBase; }
                catch (System.InvalidOperationException) { story.SkinColorBase = Color.white; }
            }
            if (Extension != null)
            {
                // 检查story是否是首次初始化
                bool isStoryFirstInit = story == null;

                outfits ??= new Pawn_OutfitTracker(this);
                story ??= new Pawn_StoryTracker(this);

                // 仅在首次初始化时设置这些值，避免覆盖加载的数据
                if (isStoryFirstInit)
                {
                    story.bodyType ??= Extension.bodyTypeOverride;
                    story.headType ??= Extension.headTypeOverride;
                    story.SkinColorBase = Color.white;
                    story.HairColor = Color.white;

                    // 如果不允许改变髮型，强制设置为秃头；否则仅在未初始化时设置
                    if (!Extension.canChangeHairStyle || story.hairDef == null)
                    {
                        story.hairDef = HairDefOf.Bald;
                    }
                }

                style ??= new Pawn_StyleTracker(this)
                {
                    beardDef = BeardDefOf.NoBeard,
                    FaceTattoo = null,
                    BodyTattoo = null,
                };

                interactions ??= new(this);
                if (skills == null)
                {
                    skills = new(this);
                    skills.skills.ForEach(s => s.Level = def.race.mechFixedSkillLevel);
                    if (!Extension.skills.NullOrEmpty())
                    {
                        foreach (SkillRange item in Extension.skills)
                        {
                            skills.GetSkill(item.Skill).Level = item.Range.RandomInRange;
                        }
                    }
                }

                // 初始化工作设置，让机械体能够被分配工作
                if (workSettings == null)
                {
                    workSettings = new Pawn_WorkSettings(this);
                    workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                    ApplyWorkTypeRestrictions();
                }
            }
        }
        // 限制机械体工作类型
        public void ApplyWorkTypeRestrictions()
        {
            if (workSettings == null) return;
            if (RaceProps.IsMechanoid || RaceProps.mechEnabledWorkTypes.NullOrEmpty()) return;
            foreach (WorkTypeDef w in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (!RaceProps.mechEnabledWorkTypes.Contains(w))
                {
                    workSettings.SetPriority(w, 0);
                }
            }
        }
        public void Equip(ThingWithComps equipment)
        {
            equipment.SetForbidden(false);
            this.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Equip, equipment), JobTag.DraftedOrder);
        }

        public void Wear(ThingWithComps apparel)
        {
            apparel.SetForbidden(false);
            this.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wear, apparel), JobTag.DraftedOrder);
        }
    }
}