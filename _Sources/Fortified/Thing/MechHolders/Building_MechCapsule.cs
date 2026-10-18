// 当白昼倾坠之时
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Fortified
{
    // 通用机兵休眠容器
    // 可容纳任意尺寸的机兵，自动绘制内部机兵
    public class Building_MechCapsule : Building, IThingHolder
    {
        #region 字段

        public ThingOwner<Pawn> innerContainer;

        #endregion

        #region 构造函数

        public Building_MechCapsule()
        {
            innerContainer = new ThingOwner<Pawn>(this, false, LookMode.Deep);
            innerContainer.dontTickContents = true;
        }

        #endregion

        #region 属性

        public bool HasMech => innerContainer != null && innerContainer.Count > 0;

        public Pawn Mech => HasMech ? innerContainer[0] : null;

        private ModExtension_MechCapsule Extension => def.GetModExtension<ModExtension_MechCapsule>();

        #endregion

        #region IThingHolder

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }

        #endregion

        #region 生命周期

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            // 遗迹生成模式：如果没有机兵且有配置，随机生成一个
            if (!respawningAfterLoad && !HasMech)
            {
                TryGenerateRandomMech();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);

            if (innerContainer == null)
            {
                innerContainer = new ThingOwner<Pawn>(this, false, LookMode.Deep);
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (mode != DestroyMode.Vanish)
            {
                innerContainer.ClearAndDestroyContents();
            }
            base.Destroy(mode);
        }

        #endregion

        #region 公共方法

        // 尝试放入机兵
        public bool TryAcceptMech(Pawn mech)
        {
            if (mech == null) return false;
            if (HasMech)
            {
                Log.Warning("[FFF] MechCapsule already contains a mech");
                return false;
            }

            return innerContainer.TryAdd(mech);
        }

        // 解析真正负责带宽的机械师：
        // 一般殖民者就是自己；统御者（军士等 OverseerMech、司令核心等 Building_Overseer）则是它的 dummy pawn。
        // Resolve whose bandwidth pays for the mech: the colonist itself, or an overseer's dummy mechanitor.
        public static Pawn ResolveController(Thing actor)
        {
            if (actor is IOverseer overseer)
            {
                return overseer.Comp != null && overseer.Comp.MechanitorActive ? overseer.Comp.dummyPawn : null;
            }
            if (actor is Pawn pawn && MechanitorUtility.IsMechanitor(pawn))
            {
                return pawn;
            }
            return null;
        }

        // 检查 actor 现在能否启动舱内机兵，失败时附带原因供 UI 显示
        public AcceptanceReport CanActivate(Thing actor)
        {
            if (!HasMech) return false;

            Pawn controller = ResolveController(actor);
            if (controller == null)
            {
                return "FFF.Reason.NotMechanitor".Translate();
            }

            float bandwidthCost = Mech.GetStatValue(StatDefOf.BandwidthCost);
            float availableBandwidth = controller.mechanitor.TotalBandwidth - controller.mechanitor.UsedBandwidth;
            if (availableBandwidth < bandwidthCost)
            {
                return "FFF.Reason.NeedBandwidth".Translate(bandwidthCost);
            }
            return true;
        }

        // 激活机兵（由机械师或统御者控制）
        public void ActivateMech(Thing actor)
        {
            if (!HasMech)
            {
                Log.Error("[FFF] ActivateMech: no mech in capsule");
                return;
            }

            Pawn controller = ResolveController(actor);
            if (controller == null)
            {
                Log.Error("[FFF] ActivateMech: invalid mechanitor");
                return;
            }

            Pawn mech = Mech;

            // 检查带宽
            float bandwidthCost = mech.GetStatValue(StatDefOf.BandwidthCost);
            float availableBandwidth = controller.mechanitor.TotalBandwidth - controller.mechanitor.UsedBandwidth;
            if (availableBandwidth < bandwidthCost)
            {
                Messages.Message("FFF.NeedMoreBandwidth".Translate(bandwidthCost), actor, MessageTypeDefOf.RejectInput);
                return;
            }

            if (actor is IOverseer overseer)
            {
                // 统御者：交给 CompOverseer.Connect，它会一并处理派系、旧 overseer 关系与带宽通知
                overseer.Comp.Connect(mech);
            }
            else
            {
                // 设置派系
                mech.SetFaction(Faction.OfPlayer);

                // 建立 overseer 关系
                controller.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
            }

            // Non-player generation can cache unrestricted work types. Rebuild after either control path.
            mech.Notify_DisabledWorkTypesChanged();

            // 释放机兵
            innerContainer.TryDropAll(Position, Map, ThingPlaceMode.Near);

            // 销毁容器
            Destroy(DestroyMode.Vanish);

            Messages.Message("FFF.MechActivated".Translate(mech.LabelCap, actor.LabelShort), mech, MessageTypeDefOf.PositiveEvent);
        }

        // 警報／遭遇時的敵對啟動：機兵以指定陣營（預設容器自身陣營）醒來、掛上攻擊或防守 Lord，容器銷毀。回傳醒來的機兵，失敗回 null。
        // Hostile wake-up (alarms, ambushes): the mech comes out under the given faction (defaulting to the capsule's),
        // gets an assault or defend lord, and the capsule is destroyed. Returns the mech, or null if nothing happened.
        public Pawn ReleaseHostile(Faction faction = null)
        {
            if (!HasMech || !Spawned) return null;

            Pawn mech = Mech;
            Faction owner = faction ?? Faction ?? mech.Faction ?? Faction.OfAncientsHostile;
            if (mech.Faction != owner)
            {
                mech.SetFaction(owner);
            }

            Map map = Map;
            IntVec3 pos = Position;
            innerContainer.TryDropAll(pos, map, ThingPlaceMode.Near);

            if (mech.Spawned && owner != null && owner != Faction.OfPlayer)
            {
                LordJob lordJob = owner.HostileTo(Faction.OfPlayer)
                    ? new LordJob_AssaultColony(owner, canKidnap: false, canTimeoutOrFlee: false, sappers: false, useAvoidGridSmart: false, canSteal: false)
                    : (LordJob)new LordJob_DefendPoint(pos);
                LordMaker.MakeNewLord(owner, lordJob, map, new List<Pawn> { mech });
            }

            Destroy(DestroyMode.Vanish);
            return mech;
        }

        // 弹出并销毁机兵
        public void EjectAndDestroy(Pawn actor)
        {
            if (!HasMech) return;

            Pawn mech = Mech;
            innerContainer.TryDrop(mech, ThingPlaceMode.Near, 1, out _);

            if (mech != null && !mech.Dead)
            {
                mech.SetFactionDirect(Faction.OfAncients);
                mech.Kill(new DamageInfo(DamageDefOf.ExecutionCut, 200));
            }

            Destroy(DestroyMode.Vanish);
        }

        #endregion

        #region 右键菜单

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            if (!HasMech) yield break;

            // 统御者机兵（军士等）的选项由 FloatMenuOptionProvider_OverseerMech 提供，这里只处理人类机械师
            if (selPawn is IOverseer) yield break;

            if (!selPawn.CanReach(this, PathEndMode.InteractionCell, Danger.Deadly))
            {
                yield return new FloatMenuOption("FFF.CannotReach".Translate(), null);
                yield break;
            }

            // 控制选项
            if (selPawn.WorkTypeIsDisabled(WorkTypeDefOf.Research))
            {
                yield return CreateDisabledOption("FFF.Reason.WorkTypeDisabled".Translate());
                yield break;
            }

            yield return GetActivateOption(selPawn);
        }

        // 「控制 {机兵}」选项：人类机械师与统御者机兵共用，可达性由调用方先行检查
        public FloatMenuOption GetActivateOption(Pawn selPawn)
        {
            AcceptanceReport report = CanActivate(selPawn);
            if (!report.Accepted)
            {
                return CreateDisabledOption(report.Reason);
            }

            return new FloatMenuOption("FFF.DeactivatedMech_Control".Translate(Mech.LabelCap), delegate
            {
                selPawn.jobs.TryTakeOrderedJob(new Job(FFF_DefOf.FFF_HackMechCapsule, this));
            });
        }

        private FloatMenuOption CreateDisabledOption(string reason)
        {
            return new FloatMenuOption("FFF.DeactivatedMech_CannotControl".Translate() + ": " + reason, null);
        }

        #endregion

        #region 属性重写

        // 显示机兵名字而非建筑名字
        public override string Label
        {
            get
            {
                if (HasMech)
                {
                    return Mech.LabelCap;
                }
                return base.Label;
            }
        }

        // 缓存机兵图形
        private Graphic cachedMechGraphic;

        // 返回机兵图形
        public override Graphic Graphic
        {
            get
            {
                Pawn mech = Mech;
                if (mech != null)
                {
                    if (cachedMechGraphic == null)
                    {
                        cachedMechGraphic = mech.Drawer?.renderer?.BodyGraphic;
                    }
                    if (cachedMechGraphic != null) return cachedMechGraphic;
                }
                return base.Graphic;
            }
        }

        #endregion

        #region 绘制

        // 绘制机兵图形
        public override void Print(SectionLayer layer)
        {
            if (HasMech)
            {
                // 绘制机兵图形
                Graphic mechGraphic = Mech.Drawer.renderer.BodyGraphic;
                if (mechGraphic != null)
                {
                    mechGraphic.Print(layer, this, 0f);
                }
            }
            else
            {
                // 没有机兵时绘制默认图形
                base.Print(layer);
            }
        }

        public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            if (HasMech && phase == DrawPhase.Draw)
            {
                Vector3 mechDrawLoc = drawLoc;
                if (Extension != null)
                {
                    mechDrawLoc += Extension.innerPawnDrawOffset;
                }

                Mech.Drawer.renderer.DynamicDrawPhaseAt(phase, mechDrawLoc, Rotation, false);
            }
            else
            {
                base.DynamicDrawPhaseAt(phase, drawLoc, flip);
            }
        }

        public override string GetInspectString()
        {
            string text = base.GetInspectString();

            if (HasMech)
            {
                if (!text.NullOrEmpty()) text += "\n";
                text += "FFF.ContainsMech".Translate(Mech.LabelCap);
            }

            return text;
        }

        #endregion

        #region 私有方法

        // 遗迹生成模式：随机生成机兵
        private void TryGenerateRandomMech()
        {
            var ext = Extension;
            if (ext?.possibleGeneratePawn == null || ext.possibleGeneratePawn.Count == 0 || !Rand.Chance(ext.spawnChance))
            {
                if (!HasMech) Destroy(DestroyMode.Vanish);
                return;
            }

            GenerateAndSetupMech(ext);
        }

        private void GenerateAndSetupMech(ModExtension_MechCapsule ext)
        {
            PawnGenOption selected = ext.possibleGeneratePawn.RandomElementByWeight(p => p.selectionWeight);
            if (selected?.kind == null) return;

            Pawn mech = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                selected.kind, Faction ?? Faction.OfAncients ?? Faction.OfPirates,
                PawnGenerationContext.NonPlayer, Map?.Tile ?? -1
            ));

            if (mech == null) { Destroy(DestroyMode.Vanish); return; }
            SetupPawnVisuals(mech);
            SetupPawnInventory(mech, ext);
            ApplyRandomDamage(mech, ext);
            innerContainer.TryAdd(mech);
        }

        private void SetupPawnVisuals(Pawn mech)
        {
            if (mech.kindDef?.nameMaker != null)
                mech.Name = PawnBioAndNameGenerator.GenerateFullPawnName(mech.def, mech.kindDef.nameMaker);
        }

        private void SetupPawnInventory(Pawn mech, ModExtension_MechCapsule ext)
        {
            if (!Rand.Chance(ext.weaponChance) && mech.equipment?.Primary != null && !mech.equipment.Primary.def.destroyOnDrop)
            {
                mech.equipment.DestroyAllEquipment();
                mech.inventory?.DestroyAll();
            }
        }

        private void ApplyRandomDamage(Pawn mech, ModExtension_MechCapsule ext)
        {
            if (ext.damageHediffs == null || ext.damageHediffs.Count == 0) return;

            int damageCount = ext.damageCount.RandomInRange;
            if (damageCount <= 0) return;

            for (int i = 0; i < damageCount; i++)
            {
                var parts = mech.RaceProps?.body?.AllParts;
                if (parts == null) break;

                var validParts = new List<BodyPartRecord>();
                foreach (var part in parts)
                {
                    if (!part.IsCorePart && !mech.health.hediffSet.HasMissingPartFor(part))
                    {
                        validParts.Add(part);
                    }
                }

                if (validParts.Count == 0) break;

                var targetPart = validParts.RandomElement();
                var hediffDef = ext.damageHediffs.RandomElement();
                mech.health.AddHediff(hediffDef, targetPart);
            }
        }

        #endregion
    }
}
