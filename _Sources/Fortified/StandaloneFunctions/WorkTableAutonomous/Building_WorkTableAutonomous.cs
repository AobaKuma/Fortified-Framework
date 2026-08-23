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

        /// <summary>連結的 facility。抽料需要走這個清單，見 <see cref="LinkedStorageIngredientPuller"/>。</summary>
        public CompAffectedByFacilities CompFacility => compFacility;

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
            // 機台可能已經自己抽料開工了（pullFromLinkedStorage），這時候半路趕到的殖民者
            // 不可以把進行中的訂單蓋掉——那會把已經跑掉的加工進度整個歸零。
            if (activeBill != null && activeBill != bill)
            {
                return;
            }
            if (activeBill == bill && prepared)
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
                // 原料已經變成產品了，要照原版 ConsumeIngredients 的做法真的銷毀。
                // 先前用的 Clear() 只是把它們從容器移除：既沒 spawn 也沒 Destroy，
                // 變成沒人管的無主物件，Comp 的收尾（CompRottable、CompArt 之類）也不會跑。
                innerContainer.ClearAndDestroyContents();
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
            // 夾到至少 1：GetInspectString 會拿它當除數算剩餘階段數，0 會得到 Infinity，
            // 再丟給 Mathf.CeilToInt 就是未定義行為（實務上會變成 int.MinValue 之類的鬼數字）。
            return Mathf.Max(1f, modExtension.workAmountPerStage);
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
        /// <summary>
        /// 自動抽料：從連結的儲存建築把料備齊，備齊後直接開工，全程不需要殖民者。
        /// </summary>
        /// <remarks>
        /// 只要有殖民者預約了這台機器就整個跳過。這一條把「小人正搬著料走過來，機台卻自己先開工」
        /// 那一整類競態關掉了——不然小人到場時會把第二份原料倒進容器，那些料會被 Finish() 白吃掉。
        /// </remarks>
        protected void TryPullAndStart()
        {
            ModExtension_AutoWorkTable ext = modExtension;
            if (ext == null || !ext.pullFromLinkedStorage) return;
            if (!LinkedStorageIngredientPuller.Available) return;
            if (compFacility == null) return;

            // 正在做事、或已經備妥等著跑，就別插手。
            if (activeBill != null || prepared) return;
            if (!CanRun) return;
            if (CompBreakdownable != null && CompBreakdownable.BrokenDown) return;
            if (!CurrentlyUsableForBills()) return;

            Map map = Map;
            Faction player = Faction.OfPlayer;
            if (map == null || player == null) return;
            if (map.reservationManager != null && map.reservationManager.IsReservedByAnyoneOf(this, player))
            {
                return;
            }

            BillStack stack = BillStack;
            if (stack == null || stack.Count == 0) return;

            List<Bill> bills = stack.Bills;
            for (int i = 0; i < bills.Count; i++)
            {
                if (!(bills[i] is Bill_Production bill)) continue;
                if (!CanAutoRun(bill)) continue;
                if (!LinkedStorageIngredientPuller.TryPullIngredientsFor(this, bill)) continue;

                Pawn worker = ResolveNominalWorker(bill);
                if (worker == null)
                {
                    // 料已經進來了，但整張地圖找不到可以掛名的製作者（全滅、全在商隊上）。
                    // 不開工，維持原狀等人回來——容器裡的料不會消失，下一輪會沿用。
                    Log.WarningOnce(
                        $"[FFF] {LabelCap} 已備妥原料，但找不到可用的名義製作者，暫緩自動開工。",
                        thingIDNumber ^ 0x7C41A9);
                    return;
                }

                StartBill(bill, this, worker);
                return; // 一次只開一張單
            }
        }

        /// <summary>這張訂單可不可以在沒有殖民者參與的情況下自動執行。</summary>
        private bool CanAutoRun(Bill_Production bill)
        {
            if (bill == null || bill.recipe == null) return false;
            if (bill.DeletedOrDereferenced) return false;
            if (bill.suspended) return false;

            // ShouldDoNow 是 EnvironmentalBillGate 的唯一執行點（gate 掛在它的 postfix 上）。
            // 少了這一步，自動產線會整條繞過配方宣告的環境限制。
            if (!bill.ShouldDoNow()) return false;
            if (!bill.recipe.AvailableNow) return false;

            // 玩家指定了人選／奴隸／機兵，代表他要的就是「那個人來做」，機器不該代勞。
            if (bill.PawnRestriction != null || bill.SlavesOnly || bill.MechsOnly) return false;

            return MachineSatisfiesSkillRequirements(bill.recipe);
        }

        /// <summary>
        /// 用 <see cref="ModExtension_AutoWorkTable.skills"/> 宣告的額定技能去對配方的技能門檻。
        /// 自動開工時沒有小人可以檢查，機台的額定技能就是它的資格。
        /// </summary>
        private bool MachineSatisfiesSkillRequirements(RecipeDef recipe)
        {
            List<SkillRequirement> requirements = recipe?.skillRequirements;
            if (requirements.NullOrEmpty()) return true;

            Dictionary<SkillDef, int> rated = modExtension?.skills;
            for (int i = 0; i < requirements.Count; i++)
            {
                SkillRequirement requirement = requirements[i];
                if (requirement?.skill == null) continue;

                int level = 0;
                if (rated != null)
                {
                    rated.TryGetValue(requirement.skill, out level);
                }
                if (level < requirement.minLevel) return false;
            }
            return true;
        }

        /// <summary>
        /// 自動開工時要交給 GenRecipe 的「名義製作者」。
        /// </summary>
        /// <remarks>
        /// GenRecipe.MakeRecipeProducts 會直接取用 worker.GetStatValue、Notify_RecipeProduced(worker)
        /// 與 PostProcessProduct(worker.Ideo)，傳 null 必爆，所以無論如何都得找到一個人。
        /// 選擇必須是決定性的（多人連線每台客戶端都要選到同一個），因此不用 Rand，
        /// 而是「配方技能最高、同分取 thingIDNumber 最小」。
        /// 品質不會因此變差：SetQuality 會拿機台額定技能再擲一次並取較好的一邊。
        /// </remarks>
        protected Pawn ResolveNominalWorker(Bill bill)
        {
            if (IsValidHandler(lastHandler) && lastHandler.Spawned && lastHandler.Map == Map)
            {
                return lastHandler;
            }

            Map map = Map;
            Faction player = Faction.OfPlayer;
            if (map?.mapPawns == null || player == null)
            {
                return IsValidHandler(lastHandler) ? lastHandler : null;
            }

            SkillDef skill = bill?.recipe?.workSkill;
            Pawn best = PickNominalWorker(map.mapPawns.FreeColonistsSpawned, skill);
            if (best == null)
            {
                // 純機兵殖民地：退一步找任何玩家陣營的生成單位。
                best = PickNominalWorker(map.mapPawns.SpawnedPawnsInFaction(player), skill);
            }
            return best ?? (IsValidHandler(lastHandler) ? lastHandler : null);
        }

        private static Pawn PickNominalWorker(List<Pawn> pool, SkillDef skill)
        {
            if (pool.NullOrEmpty()) return null;

            Pawn best = null;
            int bestLevel = int.MinValue;
            for (int i = 0; i < pool.Count; i++)
            {
                Pawn pawn = pool[i];
                if (!IsValidHandler(pawn)) continue;

                int level = 0;
                if (skill != null && pawn.skills != null)
                {
                    SkillRecord record = pawn.skills.GetSkill(skill);
                    if (record != null && !record.TotallyDisabled)
                    {
                        level = record.Level;
                    }
                }

                // 同分時取 thingIDNumber 較小的，讓每台客戶端算出同一個結果。
                if (best == null || level > bestLevel
                    || (level == bestLevel && pawn.thingIDNumber < best.thingIDNumber))
                {
                    best = pawn;
                    bestLevel = level;
                }
            }
            return best;
        }

        private bool effectActive = false;
        protected override void Tick()
        {
            if (!Spawned) return;
            if (CompBreakdownable != null && CompBreakdownable.BrokenDown) return;
            // Power 可能是 null：只要有 def 套了這個 thingClass 卻沒給 CompPowerTrader，
            // 底下每 250 tick 就會噴一次 NRE。CanRun 早就有做這個檢查，這裡以前漏了。
            if (Power != null && this.IsHashIntervalTick(250))
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
            // 自動抽料。放在 Tick 而不是 TickInterval：TickInterval 的觸發頻率跟鏡頭距離有關，
            // 不是決定性的，多人連線時每台客戶端會在不同 tick 開工。
            if (modExtension != null && modExtension.pullFromLinkedStorage
                && this.IsHashIntervalTick(Mathf.Max(1, modExtension.pullCheckIntervalTicks)))
            {
                TryPullAndStart();
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
            // 沒 spawn 就沒有地圖可以放，TryDropAll 傳 null map 會炸。
            // 被拆成 minified 或還在建造中的時候都可能走到這裡。
            if (Spawned && Map != null)
            {
                innerContainer.TryDropAll(base.Position, base.Map, ThingPlaceMode.Near);
            }
            if (Power != null)
            {
                Power.PowerOutput = -Power.Props.idlePowerDraw;
            }
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
