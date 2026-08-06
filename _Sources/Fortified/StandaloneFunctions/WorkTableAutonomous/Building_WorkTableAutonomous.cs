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
            if (handler != null) lastHandler = handler;
            if (totalWorkAmount <= 0f)
            {
                ThingPlaceMode placeMode = modExtension?.ejectPlaceMode ?? ThingPlaceMode.Near;
                List<Thing> list = new();
                innerContainer.CopyToList(list);
                foreach (Thing item in GenRecipe.MakeRecipeProducts(activeBill.recipe, handler, list, CalculateDominantIngredient(list), this))
                {
                    if (item.TryGetComp<CompQuality>(out var q))
                    {
                        SetQuality(q);
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
        public override void Notify_BillDeleted(Bill bill)
        {
            Messages.Message("FFF.Autofacturer.WorkerCanceled".Translate(Label), this, MessageTypeDefOf.RejectInput);
            base.Notify_BillDeleted(bill);
        }
        protected void SetQuality(CompQuality comp)
        {
            QualityCategory q = comp.Quality;
            if (!compFacility.LinkedFacilitiesListForReading.NullOrEmpty())
            {
                var li = compFacility.LinkedFacilitiesListForReading.FindAll(b => b.def.HasModExtension<ModExtension_QualityChance>());
                if (!li.Any()) return;
                foreach (Thing building in li)
                {
                    if (building.TryGetComp<CompPowerTrader>(out var c) && !c.PowerOn) continue;

                    if (q != QualityCategory.Legendary && Rand.Chance(building.def.GetModExtension<ModExtension_QualityChance>().qualityChance))
                    {
                        q++;
                    }
                }
            }
            comp.SetQuality(q, null);
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
            if (lastHandler == null || lastHandler.Destroyed) return;

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
