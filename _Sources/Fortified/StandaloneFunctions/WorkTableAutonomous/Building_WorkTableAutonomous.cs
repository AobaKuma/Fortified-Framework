using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse.Sound;
using Verse;
using UnityEngine;
using Verse.Noise;
using Multiplayer.API;

namespace Fortified
{
    public class Building_WorkTableAutonomous : Building_WorkTable, IThingHolder, INotifyHauledTo
    {
        public CompPowerTrader Power;
        public CompBreakdownable CompBreakdownable;

        public ThingOwner innerContainer;

        public Bill_Production activeBill;
        public float totalWorkAmount;
        public float curWorkAmount;
        public bool prepared;

        // 最後一個操作過這台機器的殖民者。自動彈出產品時需要一個「名義製作者」來決定
        // 品質與意識形態風格 —— GenRecipe.PostProcessProduct 會直接存取 worker.Ideo，
        // 傳 null 會直接爆掉，所以這裡必須記住人。
        public Pawn lastHandler;

        protected Effecter Effecter => effecter ??= modExtension?.GetEffecterDef_Phase(this.Rotation)?.SpawnMaintained(this, Map);
        private Effecter effecter;
        private int maintainTick = 0;
        private CompAffectedByFacilities compFacility;

        public bool CanRun => Power == null || Power.PowerOn;

        public ModExtension_AutoWorkTable modExtension = null;
        

        public Building_WorkTableAutonomous()
        {
            innerContainer = new ThingOwner<Thing>(this);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            this.TryGetComp(out Power);
            this.TryGetComp(out CompBreakdownable);
            this.TryGetComp(out compFacility);
            modExtension = def.GetModExtension<ModExtension_AutoWorkTable>();
            maintainTick = Rand.Range(0, 120);
        }

        public void StartBill(Bill_Production bill, Thing thing, Pawn handler)
        {
            if (bill == null) 
            {
                return;
            }
            activeBill = bill;
            if (handler != null) lastHandler = handler;
            totalWorkAmount = bill.GetWorkAmount(thing);

            float factor = 1 / this.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor, true);
            //Log.Message($"WorkTableEfficiencyFactor: {this.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor, true)}");
            totalWorkAmount *= factor;
            ResetCurWorkAmount(handler);
            prepared = true;
        }

        public void Finish(Pawn handler)
        {
            if (activeBill == null) return;
            if (IsValidHandler(handler)) lastHandler = handler;
            if (totalWorkAmount <= 0f)
            {
                // GenRecipe.PostProcessProduct 會直接存取 worker.Ideo / worker.RaceProps，傳 null 必爆。
                // 這裡退回 lastHandler，兩者都不可用就什麼都不做 —— 保留 activeBill，
                // 讓 WorkGiver 下次派人來收，總比整個 tick 噴紅字好。
                Pawn worker = IsValidHandler(handler) ? handler : (IsValidHandler(lastHandler) ? lastHandler : null);
                if (worker == null)
                {
                    Log.WarningOnce($"[FFF] {this.LabelCap} finished a bill with no usable handler; deferring product ejection.", this.thingIDNumber ^ 0x5F3A11);
                    return;
                }

                ThingPlaceMode placeMode = modExtension?.ejectPlaceMode ?? ThingPlaceMode.Near;
                List<Thing> list = new();
                innerContainer.CopyToList(list);
                foreach (Thing item in GenRecipe.MakeRecipeProducts(activeBill.recipe, worker, list, CalculateDominantIngredient(list), this))
                {
                    if (item.TryGetComp<CompQuality>(out var q))
                    {
                        SetQuality(q, activeBill.recipe);
                    }
                    GenPlace.TryPlaceThing(item, this.InteractionCell,
                        base.Map, placeMode, null, null, null, 30);
                }
                if (activeBill.repeatMode == BillRepeatModeDefOf.RepeatCount)
                {
                    activeBill.repeatCount--;
                }
                if (activeBill.repeatCount == 0)
                {
                    Messages.Message("FFF.Autofacturer.WorkerDone".Translate(activeBill.Label), this, MessageTypeDefOf.TaskCompletion);
                }
                activeBill = null;
                totalWorkAmount = 0f;
                innerContainer.Clear();

            }
            else
            {
                ResetCurWorkAmount(handler);
                prepared = true;
            }
        }
        /// <summary>
        /// 能不能拿這個小人當「名義製作者」丟進 GenRecipe。
        /// 死掉的人不該再被記為製作者（也會讓 TaleRecorder 記下奇怪的紀錄）。
        /// </summary>
        protected static bool IsValidHandler(Pawn pawn)
        {
            return pawn != null && !pawn.Destroyed && !pawn.Dead && pawn.RaceProps != null;
        }

        public override void Notify_BillDeleted(Bill bill)
        {
            Messages.Message("FFF.Autofacturer.WorkerCanceled".Translate(Label), this, MessageTypeDefOf.RejectInput);
            base.Notify_BillDeleted(bill);
        }
        protected void SetQuality(CompQuality comp, RecipeDef recipe = null)
        {
            // GenRecipe 已經依「剛好過來收件的那個小人」的技能擲過一次品質。
            // 但這是台自動化機台，ModExtension_AutoWorkTable.skills 宣告的額定技能才是它的加工水準；
            // 先前這個欄位完全沒有被讀取，導致低技能小人（或機兵）收件就固定產出爛貨，
            // 接再多機械櫃也只是從糟糕往上加一兩階。
            // 這裡用額定技能再擲一次，取兩者較好的一邊 —— 高技能工匠不會因此變差。
            QualityCategory q = comp.Quality;
            int ratedLevel = RatedSkillLevel(recipe);
            if (ratedLevel >= 0)
            {
                QualityCategory machineQuality = QualityUtility.GenerateQualityCreatedByPawn(ratedLevel, false);
                if (machineQuality > q)
                {
                    q = machineQuality;
                }
            }

            // 機械櫃：每一台通電的櫃子各有一次提升一階的機會。
            if (compFacility != null && !compFacility.LinkedFacilitiesListForReading.NullOrEmpty())
            {
                foreach (Thing building in compFacility.LinkedFacilitiesListForReading)
                {
                    if (building?.def == null) continue;
                    var ext = building.def.GetModExtension<ModExtension_QualityChance>();
                    if (ext == null) continue;
                    if (building.TryGetComp<CompPowerTrader>(out var c) && !c.PowerOn) continue;

                    if (q != QualityCategory.Legendary && Rand.Chance(ext.qualityChance))
                    {
                        q++;
                    }
                }
            }
            comp.SetQuality(q, ArtGenerationContext.Colony);
        }

        /// <summary>
        /// 這台機器對該配方相關技能的額定等級，未宣告時回傳 -1。
        /// </summary>
        private int RatedSkillLevel(RecipeDef recipe)
        {
            SkillDef skill = recipe?.workSkill;
            if (skill == null || modExtension?.skills == null)
            {
                return -1;
            }
            if (!modExtension.skills.TryGetValue(skill, out int level))
            {
                return -1;
            }
            return Mathf.Clamp(level, 0, 20);
        }

        public int GetWorkTime()//互動的工作時間
        {
            if (modExtension == null) return 300;
            return modExtension.workTime;
            
        }
        public float GetWorkAmountStage()
        {
            if (modExtension == null) return 60000;
            return (float)modExtension.workAmountPerStage;
        }

        private void ResetCurWorkAmount(Pawn handler)
        {
            float workAmount = GetWorkAmountStage();
            if (totalWorkAmount > workAmount)
            {
                curWorkAmount = workAmount;
                totalWorkAmount -= workAmount;
            }
            else
            {
                curWorkAmount = totalWorkAmount;
                totalWorkAmount = 0f;
            }

            if (curWorkAmount <= 0f)
            {
                curWorkAmount = 0f;
                prepared = false;
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            if (curWorkAmount > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "FFF.CancelActiveBill".Translate(),
                    defaultDesc = "FFF.CancelActiveBillDesc".Translate(),
                    icon = FFF_Icons.icon_Cancel,
                    action = delegate
                    {
                        [SyncMethod] void SyncCancelBill() { Cancel(); }
                        SyncCancelBill();
                    }
                };
            }
            if (DebugSettings.godMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Test Done Trigger",
                    icon = FFF_Icons.icon_Cancel,
                    action = delegate
                    {
                        [SyncMethod] void SyncTestDone() { var v = this.modExtension.GetEffecterDef_DoneTrigger(this.Rotation)?.SpawnMaintained(this, this);
                        v.Trigger(this, this); }
                        SyncTestDone();
                    }
                };
                yield return new Command_Action
                {
                    defaultLabel = "Test Phase Trigger",
                    icon = FFF_Icons.icon_Cancel,
                    action = delegate
                    {
                        [SyncMethod] void SyncTestDone() { PlayEffecter(); }
                        SyncTestDone();
                    }
                };
            }
        }

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (!prepared || !CanRun) return;
            curWorkAmount -= delta * (this.GetStatValue(StatDefOf.WorkTableEfficiencyFactor) > 1 ? this.GetStatValue(StatDefOf.WorkTableEfficiencyFactor) : 1);
            if (curWorkAmount <= 0f)
            {
                curWorkAmount = 0f;
                prepared = false;
                if (totalWorkAmount <= 0f)
                {
                    modExtension?.GetEffecterDef_DoneTrigger(Rotation)?.SpawnAttached(this, Map).Trigger(this, this);
                    TryAutoEject();
                }
            }
        }

        // 開啟 autoEjectProducts 時，最後一個階段跑完就直接結算並把產品丟出來。
        // 找不到可用的名義製作者就什麼都不做，退回原本等人來收的流程。
        protected void TryAutoEject()
        {
            if (modExtension == null || !modExtension.autoEjectProducts) return;
            if (activeBill == null || totalWorkAmount > 0f) return;
            if (!IsValidHandler(lastHandler)) return;

            modExtension.ejectSound?.PlayOneShot(new TargetInfo(Position, Map));
            Finish(lastHandler);
        }
        private bool effectActive = false;
        protected override void Tick()
        {
            if (!Spawned) return;
            if (CompBreakdownable != null && CompBreakdownable.BrokenDown) return;
            if (this.IsHashIntervalTick(250))
            {
                if (activeBill != null && prepared)
                {
                    Power.PowerOutput = 0f - Power.Props.PowerConsumption;
                }
                else
                {
                    Power.PowerOutput = 0f - Power.Props.idlePowerDraw;
                }
            }
            if (this.IsHashIntervalTick(3))
            {
                if (activeBill != null && prepared && CanRun)
                {
                    if (maintainTick > 0) maintainTick--;
                    else
                    {
                        effectActive = !effectActive;
                        maintainTick = Effecter?.def.maintainTicks ?? 0;
                    }
                    if (effectActive) PlayEffecter();
                }
            }
        }
        public override void TickRare()
        {
            base.TickRare();
        }
        protected void PlayEffecter()
        {
            if (modExtension == null) return;

            Effecter?.EffectTick(this, this);
            if (modExtension.activeMote != null && (!modExtension.northOnly || this.Rotation == Rot4.North))
            {
                MoteMaker.MakeAttachedOverlay(this, modExtension.activeMote, Vector3.zero);
            }
        }
        public void Cancel()
        {
            prepared = false;
            totalWorkAmount = 0f;
            curWorkAmount = 0f;
            activeBill = null;
            innerContainer.TryDropAll(base.Position, base.Map, ThingPlaceMode.Near);
            Power.PowerOutput = -Power.Props.idlePowerDraw;
        }

        private Thing CalculateDominantIngredient(List<Thing> ingredients)
        {
            if (ingredients.NullOrEmpty())
            {
                return null;
            }
            RecipeDef recipe = activeBill.recipe;
            if (recipe.productHasIngredientStuff)
            {
                return ingredients[0];
            }
            if (recipe.products.Any((ThingDefCountClass x) => x.thingDef.MadeFromStuff) || (recipe.unfinishedThingDef != null && recipe.unfinishedThingDef.MadeFromStuff))
            {
                return ingredients.Where((Thing x) => x.def.IsStuff).RandomElementByWeight((Thing x) => x.stackCount);
            }
            return ingredients.RandomElementByWeight((Thing x) => x.stackCount);
        }

        public override string GetInspectString()
        {
            StringBuilder stringBuilder = new StringBuilder(base.GetInspectString());
            if (activeBill != null)
            {
                stringBuilder.AppendInNewLine("FFF.Autofacturer.CurrentRecipe".Translate(activeBill.Label));
                if (prepared)
                {
                    stringBuilder.AppendInNewLine("FFF.Autofacturer.Information".Translate(((int)curWorkAmount).ToStringTicksToPeriodVerbose(true, true), Mathf.CeilToInt(totalWorkAmount / GetWorkAmountStage())));
                }
                else
                {
                    if (totalWorkAmount == 0)
                    {
                        stringBuilder.AppendInNewLine("FFF.Autofacturer.WorkerFinished".Translate(Label));
                    }
                    else stringBuilder.AppendInNewLine("FFF.Autofacturer.Prepared".Translate());
                }
            }
            return stringBuilder.ToString().Trim();
        }
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }
        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref prepared, "prepared", defaultValue: false);
            Scribe_Values.Look(ref totalWorkAmount, "totalWorkAmount", 0f);
            Scribe_Values.Look(ref curWorkAmount, "curWorkAmount", 0f);
            Scribe_References.Look(ref activeBill, "activeBill");
            Scribe_References.Look(ref lastHandler, "lastHandler");
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
        }
        public void Notify_HauledTo(Pawn hauler, Thing thing, int count)
        {
            this.innerContainer.TryAddOrTransfer(thing,count);
        }
    }
}
