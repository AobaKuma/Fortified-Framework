using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Fortified
{
    /// <summary>
    /// 讓 <see cref="Building_WorkTableAutonomous"/> 直接從「連結的儲存建築」抽取配方原料，
    /// 不必等殖民者跑一趟搬運。來源必須在 def 上掛 <see cref="ModExtension_MaterialSource"/>。
    /// </summary>
    /// <remarks>
    /// 全部靜態、無狀態（暫存清單只在單次呼叫內有效），只從主執行緒的 Tick 呼叫。
    /// 任何一步失敗都退回「什麼都沒發生」，機台會回到原本等殖民者送料的流程。
    /// </remarks>
    public static class LinkedStorageIngredientPuller
    {
        /// <summary>
        /// 對應 <c>WorkGiver_DoBill.TryFindBestBillIngredientsInSet</c>。
        /// </summary>
        private delegate bool IngredientsInSetDelegate(List<Thing> availableThings, Bill bill, List<ThingCount> chosen,
            IntVec3 rootCell, bool alreadySorted, List<IngredientCount> missingIngredients);

        // 原版的挑料核心是 private static，但它的簽章完全不需要 Pawn —— 只有外層的
        // TryFindBestIngredientsHelper 才用 pawn 做 CanReserve / 可達性 / forbidden 判定。
        // 借用它可以原封不動沿用 allowMixingIngredients、CountRequiredOfFor、ingredientFilter
        // 與距離排序，不必自己維護一套會隨版本走鐘的配料演算法。
        //
        // 用反射而不是 publicizer，是為了拿到「取不到就整個功能停用」這個退路：
        // 直接呼叫在方法簽章變動時會變成 JIT 期的 MissingMethodException，那會連帶把工作台弄死。
        private static readonly IngredientsInSetDelegate selectIngredients;

        private static readonly List<Thing> candidates = new List<Thing>();
        private static readonly List<Thing> scratch = new List<Thing>();
        private static readonly HashSet<Thing> seen = new HashSet<Thing>();
        private static readonly List<ThingCount> chosen = new List<ThingCount>();

        // 重入保護：挑料核心用的是原版的 static 暫存（availableCounts），巢狀呼叫會互相踩。
        private static bool running;

        /// <summary>功能是否可用。false 時所有呼叫都會直接回傳 false，不做任何事。</summary>
        public static bool Available => selectIngredients != null;

        static LinkedStorageIngredientPuller()
        {
            try
            {
                MethodInfo method = typeof(WorkGiver_DoBill).GetMethod(
                    "TryFindBestBillIngredientsInSet",
                    BindingFlags.NonPublic | BindingFlags.Static);

                if (method != null)
                {
                    selectIngredients = (IngredientsInSetDelegate)Delegate.CreateDelegate(
                        typeof(IngredientsInSetDelegate), method, throwOnBindFailure: false);
                }
            }
            catch (Exception ex)
            {
                selectIngredients = null;
                Log.Error("[FFF] LinkedStorageIngredientPuller 初始化時發生例外：" + ex);
            }

            if (selectIngredients == null)
            {
                Log.Error("[FFF] 找不到相容的 WorkGiver_DoBill.TryFindBestBillIngredientsInSet"
                    + "（RimWorld 版本可能已變更簽章）。自動工作台的「從連結儲存建築抽料」功能停用，"
                    + "其餘行為不受影響，機台會回到由殖民者送料的流程。");
            }
        }

        /// <summary>
        /// 嘗試把 <paramref name="bill"/> 需要的原料從連結的儲存建築搬進機台的內容器。
        /// </summary>
        /// <returns>配方所需的每一項都已備妥時回傳 true。</returns>
        /// <remarks>
        /// 只有「整份配方都湊得齊」才會動手，湊不齊一顆都不搬：半套的結果是原料被鎖死在機台裡，
        /// 既做不出東西、也不會自己回倉庫，只能靠玩家手動取消訂單才拿得回來。
        /// </remarks>
        public static bool TryPullIngredientsFor(Building_WorkTableAutonomous table, Bill bill)
        {
            if (!Available || running)
            {
                return false;
            }
            if (table == null || table.Destroyed || !table.Spawned || table.Map == null)
            {
                return false;
            }
            if (table.innerContainer == null)
            {
                return false;
            }
            if (bill == null || bill.recipe == null || bill.recipe.ingredients.NullOrEmpty())
            {
                return false;
            }

            running = true;
            try
            {
                candidates.Clear();
                chosen.Clear();
                seen.Clear();

                // 機台裡已經有的料一定要算進候選，否則會照整份配方再抽一次。
                // Finish() 是把 innerContainer 的「全部」內容當成原料丟給 GenRecipe，
                // 多抽進來的部分不會退回，等於白白吃掉。
                CollectFromInnerContainer(table);
                CollectFromLinkedSources(table);

                if (candidates.Count == 0)
                {
                    return false;
                }

                // 注意：這個呼叫會就地排序 candidates，所以傳的必須是我們自己的清單。
                if (!selectIngredients(candidates, bill, chosen, table.Position, false, null))
                {
                    return false;
                }

                return MoveChosenInto(table);
            }
            catch (Exception ex)
            {
                Log.Error($"[FFF] {table.LabelCap} 從連結儲存建築抽料時發生例外，本次略過：{ex}");
                return false;
            }
            finally
            {
                candidates.Clear();
                chosen.Clear();
                seen.Clear();
                running = false;
            }
        }

        private static void CollectFromInnerContainer(Building_WorkTableAutonomous table)
        {
            ThingOwner owner = table.innerContainer;
            for (int i = 0; i < owner.Count; i++)
            {
                Thing thing = owner[i];
                if (thing == null || thing.Destroyed || thing.stackCount <= 0)
                {
                    continue;
                }
                if (!seen.Add(thing))
                {
                    continue;
                }
                // 已經在機台裡的東西不必檢查 forbidden／預約，它們早就是這台機器的了。
                candidates.Add(thing);
            }
        }

        private static void CollectFromLinkedSources(Building_WorkTableAutonomous table)
        {
            CompAffectedByFacilities comp = table.CompFacility;
            if (comp == null)
            {
                return;
            }

            List<Thing> linked = comp.LinkedFacilitiesListForReading;
            if (linked.NullOrEmpty())
            {
                return;
            }

            for (int i = 0; i < linked.Count; i++)
            {
                Thing facility = linked[i];
                if (facility == null || facility.Destroyed || !facility.Spawned || facility.def == null)
                {
                    continue;
                }
                if (facility.Map != table.Map)
                {
                    continue;
                }

                ModExtension_MaterialSource source = facility.def.GetModExtension<ModExtension_MaterialSource>();
                if (source == null)
                {
                    continue;
                }
                if (!source.allowWhileInactive && !comp.IsFacilityActive(facility))
                {
                    continue;
                }

                // 容器型：物品收在 ThingOwner 裡（書櫃、服裝架、模組自訂的箱體）。
                if (facility is IThingHolder holder)
                {
                    scratch.Clear();
                    try
                    {
                        ThingOwnerUtility.GetAllThingsRecursively(holder, scratch, allowUnreal: false);
                        for (int j = 0; j < scratch.Count; j++)
                        {
                            TryAddCandidate(scratch[j], table, source);
                        }
                    }
                    finally
                    {
                        scratch.Clear();
                    }
                }

                // 格子型：物品實際 spawn 在地圖格上（貨架 Building_Storage 等）。
                // HeldThings 是邊走 thingGrid 邊 yield 的，稍後 SplitOff 會 DeSpawn，
                // 所以一定要先整份收進 candidates 再動手搬，不能邊迭代邊改。
                if (facility is ISlotGroupParent slotParent)
                {
                    SlotGroup group = slotParent.GetSlotGroup();
                    if (group == null)
                    {
                        continue;
                    }
                    foreach (Thing thing in group.HeldThings)
                    {
                        TryAddCandidate(thing, table, source);
                    }
                }
            }
        }

        private static void TryAddCandidate(Thing thing, Building_WorkTableAutonomous table, ModExtension_MaterialSource source)
        {
            if (thing == null || thing.Destroyed || thing.stackCount <= 0)
            {
                return;
            }
            // 一座建築同時是 IThingHolder 又是 ISlotGroupParent 時會被列舉兩次。
            if (!seen.Add(thing))
            {
                return;
            }
            // 活人不是原料。正常情況不會出現在儲存建築裡，純粹是保險。
            if (thing is Pawn)
            {
                return;
            }

            Faction player = Faction.OfPlayer;
            if (player != null && thing.IsForbidden(player))
            {
                return;
            }

            if (source.respectReservations && player != null)
            {
                ReservationManager reservations = table.Map?.reservationManager;
                if (reservations != null && reservations.IsReservedByAnyoneOf(thing, player))
                {
                    return;
                }
            }

            candidates.Add(thing);
        }

        /// <summary>
        /// 把挑中的物件搬進機台。回傳 true 代表每一份都成功入庫。
        /// </summary>
        /// <remarks>
        /// 部分失敗時已經搬進去的東西會留在容器裡，不會退回——這是刻意的：
        /// 下一輪檢查會把它們算進候選，缺的部分繼續補，狀態自己會收斂。
        /// </remarks>
        private static bool MoveChosenInto(Building_WorkTableAutonomous table)
        {
            ThingOwner container = table.innerContainer;
            bool allLanded = true;

            for (int i = 0; i < chosen.Count; i++)
            {
                ThingCount thingCount = chosen[i];
                Thing thing = thingCount.Thing;

                if (thing == null || thing.Destroyed)
                {
                    allLanded = false;
                    continue;
                }
                // 早就在機台裡，不用動。
                if (thing.holdingOwner == container)
                {
                    continue;
                }

                int count = Mathf.Min(thingCount.Count, thing.stackCount);
                if (count <= 0)
                {
                    allLanded = false;
                    continue;
                }

                // SplitOff 自己會處理 DeSpawn 與「從原本的 ThingOwner 移除」，
                // 所以貨架（spawned）與容器（ThingOwner）兩種來源共用同一條路。
                Thing split = thing.SplitOff(count);
                if (split == null)
                {
                    allLanded = false;
                    continue;
                }
                if (split.Spawned)
                {
                    split.DeSpawn();
                }

                if (container.TryAdd(split, canMergeWithExistingStacks: true))
                {
                    continue;
                }

                allLanded = false;

                // 進不了容器就放回地上。寧可掉在機台旁邊，也不能讓原料憑空消失。
                if (!GenPlace.TryPlaceThing(split, table.Position, table.Map, ThingPlaceMode.Near))
                {
                    Log.WarningOnce(
                        $"[FFF] {table.LabelCap} 無法把 {split.LabelCap} 放回地圖，已銷毀以免留下無主物件。",
                        table.thingIDNumber ^ 0x2B71C3);
                    split.Destroy(DestroyMode.Vanish);
                }
            }

            return allLanded;
        }
    }
}
