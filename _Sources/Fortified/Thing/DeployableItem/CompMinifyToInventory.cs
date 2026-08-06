using RimWorld;
using System;
using Verse;
using Verse.Sound;

namespace Fortified
{
    /// <summary>
    /// 使用效果：把建築迷你化後收進使用者的裝備欄或背包。
    /// 迷你化結果若是裝備類型，會先跑完整的裝備資格檢查，不合格就改收進物品欄；
    /// 對 <see cref="IWeaponUsable"/>（機械體）另外套用載重上限。
    /// </summary>
    public class CompMinifyToInventory : CompUseEffect
    {
        private CompProperties_UseEffect Props => (CompProperties_UseEffect)props;

        public override void PrepareTick()
        {
        }

        public override void DoEffect(Pawn usedBy)
        {
            if (usedBy == null || usedBy.Destroyed)
            {
                return;
            }

            Thing thing = parent;
            if (thing == null || thing.Destroyed)
            {
                return;
            }

            if (thing is Building building && building.def.Minifiable)
            {
                // 掛上執行者 context，讓 CE 之類的兼容層知道轉換過程的溢出物該交給誰。
                using (DeployContext.Push(usedBy))
                {
                    thing = building.MakeMinified();
                }
                if (thing == null)
                {
                    Log.Warning($"[FFF] CompMinifyToInventory：{building.ToStringSafe()} 迷你化失敗，取消收納。");
                    return;
                }
            }

            if (TryEquip(usedBy, thing))
            {
                return;
            }

            StoreOrDrop(usedBy, thing);
        }

        /// <summary>
        /// 嘗試直接裝備到裝備欄。任一條件不符就回 false，由呼叫端改走背包／落地流程。
        /// </summary>
        private static bool TryEquip(Pawn usedBy, Thing thing)
        {
            if (usedBy.equipment == null)
            {
                return false;
            }
            if (thing is not ThingWithComps)
            {
                return false;
            }
            if (thing.TryGetComp<CompEquippable>() == null)
            {
                return false;
            }
            // 沒有裝備槽位的東西不該進裝備欄，直接走背包。
            if (thing.def.equipmentType == EquipmentType.None)
            {
                return false;
            }
            // 主手已有武器就不搶位，避免把原本的武器擠掉。
            if (thing.def.equipmentType == EquipmentType.Primary && usedBy.equipment.Primary != null)
            {
                return false;
            }
            if (usedBy.WorkTagIsDisabled(WorkTags.Violent))
            {
                return false;
            }

            ThingWithComps toEquip = (thing.def.stackLimit > 1 && thing.stackCount > 1)
                ? thing.SplitOff(1) as ThingWithComps
                : thing as ThingWithComps;
            if (toEquip == null)
            {
                return false;
            }

            // 完整的裝備資格檢查。
            if (!CanEquipNow(usedBy, toEquip))
            {
                // SplitOff 可能已經分離出新物件，交還給背包流程處理而非丟失。
                if (!ReferenceEquals(toEquip, thing))
                {
                    StoreOrDrop(usedBy, toEquip);
                    return true;
                }
                return false;
            }

            if (toEquip.Spawned)
            {
                toEquip.DeSpawn();
            }

            usedBy.equipment.MakeRoomFor(toEquip);
            usedBy.equipment.AddEquipment(toEquip);

            if (toEquip.def.soundInteract != null && usedBy.Map != null)
            {
                toEquip.def.soundInteract.PlayOneShot(new TargetInfo(usedBy.Position, usedBy.Map));
            }
            return true;
        }

        /// <summary>
        /// 該 pawn 現在是否真的能裝備這件東西。
        /// 走 <see cref="EquipmentUtility.CanEquip"/> 以涵蓋 vanilla 的生物編碼、靈魂鏈接綁定、
        /// 意識形態角色限制，以及本模組 Patch_EquipmentUtility_CanEquip 疊加的
        /// FFF_NeverEquip 標籤、<see cref="HeavyEquippableExtension"/> 體型門檻，
        /// 與 <see cref="IWeaponUsable"/> 的 <see cref="MechWeaponExtension"/> 武器白名單。
        /// 判定本身出錯時保守回 false，讓東西改收進物品欄而不是強行裝上。
        /// </summary>
        private static bool CanEquipNow(Pawn pawn, ThingWithComps equipment)
        {
            try
            {
                return EquipmentUtility.CanEquip(equipment, pawn, out _, checkBonded: true);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[FFF] CompMinifyToInventory：判定 {pawn.ToStringSafe()} 能否裝備 {equipment.ToStringSafe()} 時發生例外：{ex}", 0x0FF10003);
                return false;
            }
        }

        /// <summary>
        /// 收進背包；容量不足或失敗時放回地面，確保物件不會憑空消失。
        /// </summary>
        private static void StoreOrDrop(Pawn usedBy, Thing thing)
        {
            if (thing == null || thing.Destroyed)
            {
                return;
            }

            if (usedBy.inventory?.innerContainer != null)
            {
                if (HasCapacityFor(usedBy, thing))
                {
                    if (thing.Spawned)
                    {
                        thing.DeSpawn();
                    }
                    if (usedBy.inventory.innerContainer.TryAddOrTransfer(thing))
                    {
                        return;
                    }
                }
                else
                {
                    Messages.Message(
                        "CannotEquip".Translate(thing.LabelShort) + ": " + "FFF.Reason.NoPayloadCapacity".Translate(),
                        usedBy, MessageTypeDefOf.RejectInput, historical: false);
                }
            }

            if (thing.Spawned || thing.Destroyed)
            {
                return;
            }

            Map map = usedBy.MapHeld;
            if (map == null || !GenPlace.TryPlaceThing(thing, usedBy.PositionHeld, map, ThingPlaceMode.Near))
            {
                Log.Warning($"[FFF] CompMinifyToInventory：{thing.ToStringSafe()} 無法收進 {usedBy.ToStringSafe()} 的背包，也無法落地。");
            }
        }

        /// <summary>
        /// 載重檢查。僅對機械體套用，血肉 pawn 維持 vanilla 的可超載行為。
        /// </summary>
        private static bool HasCapacityFor(Pawn pawn, Thing thing)
        {
            if (pawn is not IWeaponUsable)
            {
                return true;
            }
            if (!MassUtility.CanEverCarryAnything(pawn))
            {
                return false;
            }
            float mass = thing.GetStatValue(StatDefOf.Mass) * thing.stackCount;
            return MassUtility.GearAndInventoryMass(pawn) + mass <= MassUtility.Capacity(pawn);
        }
    }
}
