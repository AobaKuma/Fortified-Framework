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

        /// <summary>
        /// 拾起資格。非己方的部署建築不得直接收走——那等同無償接收敵方裝備，
        /// 玩家必須先用 vanilla 的「宣稱」把它收為己有（宣稱本身受附近威脅限制）。
        /// </summary>
        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            return CanPickUp(parent, p);
        }

        /// <summary>
        /// 共用的拾起資格判定：拾起路徑（CompUsable 浮動選單／Gizmo）與直接裝備路徑
        /// （<see cref="FloatMenuUtility.TryMakeFloatMenuForDeployable"/> ／
        /// <see cref="JobDriver_EquipDeployable"/>）都走這裡，確保兩邊規則一致。
        /// 只限制建築；已迷你化、躺在地上的物品沿用 vanilla 的撿拾規則。
        /// </summary>
        public static AcceptanceReport CanPickUp(Thing thing, Pawn pawn)
        {
            if (thing == null || pawn == null)
            {
                return false;
            }
            if (thing is not Building building)
            {
                return true;
            }
            if (building.Faction == pawn.Faction)
            {
                return true;
            }
            return "FFF.MinifiedDeployable.NotOwned".Translate();
        }

        public override void DoEffect(Pawn usedBy)
        {
            MinifyInto(usedBy, parent, replacePrimary: false);
        }

        /// <summary>
        /// 「直接裝備」入口（浮動選單／<see cref="JobDriver_EquipDeployable"/>）。
        /// 與 <see cref="DoEffect"/> 的差別只在主手已有武器時會先騰出位置（原武器落地），
        /// 而不是把收起來的東西塞進背包。
        /// </summary>
        public static bool TryMinifyAndEquip(Pawn usedBy, Thing thing)
        {
            return MinifyInto(usedBy, thing, replacePrimary: true);
        }

        /// <summary>
        /// 把 <paramref name="thing"/>（建築或已迷你化物件）收到 <paramref name="usedBy"/> 身上。
        /// 回傳是否真的進了裝備欄。
        /// </summary>
        private static bool MinifyInto(Pawn usedBy, Thing thing, bool replacePrimary)
        {
            if (usedBy == null || usedBy.Destroyed)
            {
                return false;
            }
            if (thing == null || thing.Destroyed)
            {
                return false;
            }
            // 最後一道關卡：即使有人繞過浮動選單直接下 job，也不該收走非己方的建築。
            if (!CanPickUp(thing, usedBy).Accepted)
            {
                return false;
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
                    return false;
                }
            }

            if (TryEquip(usedBy, thing, replacePrimary))
            {
                return true;
            }

            StoreOrDrop(usedBy, thing);
            return false;
        }

        /// <summary>
        /// 嘗試直接裝備到裝備欄。任一條件不符就回 false，由呼叫端改走背包／落地流程。
        /// <paramref name="replacePrimary"/> 為 true 時，主手已有武器也照樣裝備（原武器由 MakeRoomFor 卸下落地）。
        /// </summary>
        private static bool TryEquip(Pawn usedBy, Thing thing, bool replacePrimary)
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
            if (!replacePrimary && thing.def.equipmentType == EquipmentType.Primary && usedBy.equipment.Primary != null)
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
