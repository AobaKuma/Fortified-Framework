using CombatExtended;
using Fortified;
using HarmonyLib;
using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace FortifiedCE
{
    /// <summary>
    /// 收起：建築（Building_TurretGunCE）→ 迷你化物件。
    /// 把砲塔身上的 CE 設定帶到迷你化後的武器上。
    /// </summary>
    [HarmonyPatch(typeof(MinifyUtility), "MakeMinified")]
    internal static class Harmony_MinifyUtility
    {
        [HarmonyPostfix]
        public static void PostFix(MinifiedThing __result)
        {
            if (__result == null || __result.InnerThing is not Building_TurretGunCE turretCE)
            {
                return;
            }

            DeployableCESync.Sync(
                turretCE.CompAmmo, __result.TryGetComp<CompAmmoUser>(),
                turretCE.CompFireModes, __result.TryGetComp<CompFireModes>());
        }
    }

    /// <summary>
    /// 展開：迷你化物件 → 建築（Building_TurretGunCE）。
    /// 把武器型態下的 CE 設定帶回砲塔。
    /// </summary>
    [HarmonyPatch(typeof(MinifiedThingDeployable))]
    internal static class Harmony_DeployableTurretCompat
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MinifiedThingDeployable), "DeployCECompatHook")]
        public static void PostFix(MinifiedThingDeployable minified, Thing turret)
        {
            if (minified == null || turret == null || ReferenceEquals(minified, turret))
            {
                return;
            }
            if (turret is not Building_TurretGunCE turretCE)
            {
                return;
            }

            DeployableCESync.Sync(
                minified.TryGetComp<CompAmmoUser>(), turretCE.CompAmmo,
                minified.TryGetComp<CompFireModes>(), turretCE.CompFireModes);
        }
    }

    /// <summary>
    /// 可部署物在「砲塔建築」與「迷你化武器」兩種型態之間的 CE 設定同步。
    ///
    /// 兩種型態是兩個各自獨立的 Thing，各自帶一份 CompAmmoUser / CompFireModes，
    /// 設定不會自動跟著走。玩家調好射擊模式與機會裝填閾值後收起再展開就被重置，
    /// 所以在轉換的兩個方向都做一次搬移。
    /// </summary>
    internal static class DeployableCESync
    {
        private const string LogPrefix = "[FFF-CE] Deployable 設定同步：";

        /// <summary>
        /// CompFireModes.newComp 是私有旗標；為 true 時 InitAvailableFireModes 會呼叫
        /// ResetModes() 把射擊模式打回預設。迷你化物件是當場新建的，若它的
        /// 初始化被延後到我們複製之後才執行，複製結果就會被蓋掉，所以複製完順手清掉這個旗標。
        /// 反射失敗時只是失去這層保險，不影響其他同步項目。
        /// </summary>
        private static readonly AccessTools.FieldRef<CompFireModes, bool> NewCompFlag = ResolveNewCompFlag();

        private static AccessTools.FieldRef<CompFireModes, bool> ResolveNewCompFlag()
        {
            try
            {
                if (AccessTools.Field(typeof(CompFireModes), "newComp") == null)
                {
                    Log.Warning(LogPrefix + "找不到 CompFireModes.newComp，射擊模式在少數情況下可能被 CE 重設。");
                    return null;
                }
                return AccessTools.FieldRefAccess<CompFireModes, bool>("newComp");
            }
            catch (Exception ex)
            {
                Log.Warning(LogPrefix + "解析 CompFireModes.newComp 失敗：" + ex.Message);
                return null;
            }
        }

        public static void Sync(CompAmmoUser ammoFrom, CompAmmoUser ammoTo, CompFireModes modesFrom, CompFireModes modesTo)
        {
            SwapAmmo(ammoFrom, ammoTo);
            CopyFireModes(modesFrom, modesTo);
        }

        /// <summary>
        /// 彈藥相關：機會裝填閾值、選定彈種、彈匣內容。
        /// 兩種型態的彈匣容量不一定相同，裝不下的部分不會憑空消失，
        /// 而是換算回實體彈藥交給執行者的物品欄（放不下才落地）。
        /// </summary>
        public static void SwapAmmo(CompAmmoUser from, CompAmmoUser to)
        {
            if (from == null || to == null || ReferenceEquals(from, to))
            {
                return;
            }

            try
            {
                bool sameAmmoSet = from.Props?.ammoSet == to.Props?.ammoSet;

                // 機會裝填閾值：與彈種無關的純數值偏好，一律同步。
                // 夾在 0 ~ 目標彈匣容量之間，避免兩型態彈匣大小不同時設出無效值。
                to.TryReloadOn = Mathf.Clamp(from.TryReloadOn, 0, Mathf.Max(0, to.MagSize));

                // 選定彈種：只有彈藥組相同才有意義。
                if (sameAmmoSet && from.SelectedAmmo != null)
                {
                    to.SelectedAmmo = from.SelectedAmmo;
                }

                if (!from.HasMagazine || !to.HasMagazine || !sameAmmoSet)
                {
                    return;
                }

                AmmoDef ammo = from.CurrentAmmo;
                int carried = from.CurMagCount;
                if (carried <= 0)
                {
                    return;
                }
                from.CurMagCount = 0;

                // 目標彈匣裡已經有別種彈藥時不混裝，整批走溢出流程。
                bool canMerge = to.CurMagCount <= 0 || to.CurrentAmmo == null || to.CurrentAmmo == ammo;
                int loaded = 0;
                if (canMerge)
                {
                    to.CurrentAmmo = ammo;
                    int space = Mathf.Max(0, to.MagSize - to.CurMagCount);
                    loaded = Mathf.Min(carried, space);
                    to.CurMagCount += loaded;
                }

                int overflow = carried - loaded;
                if (overflow > 0)
                {
                    PlaceOverflowAmmo(from, to, ammo, overflow);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(LogPrefix + "搬移彈藥設定時發生例外：" + ex, 0x0FF1CE10);
            }
        }

        /// <summary>
        /// 把裝不進目標彈匣的彈藥換算成實體物品，交給執行者的物品欄；收不下就落地。
        /// 換算規則沿用 CE 的 CompAmmoUser.TryUnload：一個彈藥物品代表 AmmoDef.ammoCount 發，
        /// 不足一份的零頭交給 partialUnloadAmmoDef，沒定義就跟 CE 一樣捨去。
        /// </summary>
        private static void PlaceOverflowAmmo(CompAmmoUser from, CompAmmoUser to, AmmoDef ammo, int rounds)
        {
            if (ammo == null || rounds <= 0)
            {
                return;
            }

            // 溢出物的去處：優先給正在執行收起／展開的 pawn，其次才是武器目前的持有者。
            Pawn receiver = DeployContext.CurrentWorker ?? from.Holder ?? to.Holder;
            CompInventory inventory = receiver?.TryGetComp<CompInventory>();
            Thing anchor = (Thing)receiver ?? to.parent ?? from.parent;

            int perItem = Mathf.Max(1, ammo.ammoCount);
            int fullItems = rounds / perItem;
            int remainder = rounds % perItem;

            int stackLimit = Mathf.Max(1, ammo.stackLimit);
            while (fullItems > 0)
            {
                int chunk = Mathf.Min(fullItems, stackLimit);
                fullItems -= chunk;

                Thing stack = ThingMaker.MakeThing(ammo);
                stack.stackCount = chunk;
                DeliverAmmo(stack, inventory, anchor);
            }

            if (remainder > 0 && ammo.partialUnloadAmmoDef != null)
            {
                Thing partial = ThingMaker.MakeThing(ammo.partialUnloadAmmoDef);
                partial.stackCount = remainder;
                DeliverAmmo(partial, inventory, anchor);
            }
        }

        private static void DeliverAmmo(Thing ammoThing, CompInventory inventory, Thing anchor)
        {
            if (ammoThing == null || ammoThing.Destroyed || ammoThing.stackCount <= 0)
            {
                return;
            }

            if (inventory?.container != null)
            {
                inventory.container.TryAdd(ammoThing, ammoThing.stackCount, canMergeWithExistingStacks: true);
                // 全數收進物品欄時 ammoThing 本身已被容器接管（或已被合併掉）。
                if (ammoThing.Destroyed || ammoThing.holdingOwner != null || ammoThing.stackCount <= 0)
                {
                    return;
                }
            }

            Map map = anchor?.MapHeld;
            IntVec3 pos = anchor?.PositionHeld ?? IntVec3.Invalid;
            if (map != null && pos.IsValid && GenPlace.TryPlaceThing(ammoThing, pos, map, ThingPlaceMode.Near))
            {
                return;
            }

            Log.Warning(LogPrefix + $"溢出彈藥 {ammoThing.ToStringSafe()} 既收不進物品欄也無法落地，已銷毀。");
            if (!ammoThing.Destroyed)
            {
                ammoThing.Destroy();
            }
        }

        /// <summary>
        /// 射擊模式相關：連發模式、瞄準模式、瞄準部位。
        /// 目標端已經算好可用模式清單時，只複製清單內的值，
        /// 避免把砲塔專屬模式硬塞進武器型態（或反之）而讓 CE 之後整組重設。
        /// </summary>
        public static void CopyFireModes(CompFireModes from, CompFireModes to)
        {
            if (from == null || to == null || ReferenceEquals(from, to))
            {
                return;
            }

            try
            {
                if (to.AvailableFireModes.NullOrEmpty() || to.AvailableFireModes.Contains(from.CurrentFireMode))
                {
                    to.CurrentFireMode = from.CurrentFireMode;
                }

                if (to.AvailableAimModes.NullOrEmpty() || to.AvailableAimModes.Contains(from.CurrentAimMode))
                {
                    to.CurrentAimMode = from.CurrentAimMode;
                }

                to.targetMode = from.targetMode;

                if (NewCompFlag != null)
                {
                    NewCompFlag(to) = false;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(LogPrefix + "搬移射擊模式設定時發生例外：" + ex, 0x0FF1CE11);
            }
        }
    }
}
