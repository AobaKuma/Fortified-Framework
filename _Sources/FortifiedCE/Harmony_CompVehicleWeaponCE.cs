using CombatExtended;
using Fortified;
using HarmonyLib;
using RimWorld;
using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace FortifiedCE
{
    /// <summary>
    /// CompVehicleWeapon creates and equips its defaultWeapon in PostSpawnSetup, which runs *after* PawnGenerator has
    /// already finished. Combat Extended's LoadoutPropertiesExtension.GenerateLoadoutFor therefore sees a null
    /// equipment.Primary and does neither of the two things a CE weapon needs:
    ///
    ///   - LoadWeaponWithRandAmmo, which fills the magazine (CompAmmoUser.Initialize only picks an ammo type, it leaves
    ///     curMagCount at 0)
    ///   - TryGenerateAmmoFor, which puts spare magazines in the pawn's inventory
    ///
    /// The result is a vehicle-weapon mech that spawns with an empty magazine and no ammo, i.e. unable to fire at all.
    /// This postfix does both jobs once the weapon actually exists.
    /// </summary>
    [HarmonyPatch(typeof(CompVehicleWeapon), nameof(CompVehicleWeapon.PostSpawnSetup))]
    internal static class Harmony_CompVehicleWeaponCE
    {
        [HarmonyPostfix]
        public static void Postfix(CompVehicleWeapon __instance, bool respawningAfterLoad)
        {
            if (respawningAfterLoad)
            {
                // Loaded games keep whatever was in the magazine.
                return;
            }
            try
            {
                LoadVehicleWeapon(__instance.parent as Pawn);
            }
            catch (Exception e)
            {
                Log.Error($"[FFF] Failed to load vehicle weapon ammo for {(__instance.parent as Pawn)?.LabelShortCap}: {e}");
            }
        }

        private static void LoadVehicleWeapon(Pawn pawn)
        {
            ThingWithComps gun = pawn?.equipment?.Primary;
            CompAmmoUser ammoUser = gun?.TryGetComp<CompAmmoUser>();
            if (ammoUser == null || !ammoUser.UseAmmo || ammoUser.CurMagCount > 0)
            {
                return;
            }

            AmmoDef ammo = PickAmmo(ammoUser);
            if (ammo == null)
            {
                return;
            }
            ammoUser.ResetAmmoCount(ammo);

            int spareMagazines = SpareMagazineCount(pawn);
            if (spareMagazines <= 0)
            {
                return;
            }
            CompInventory inventory = pawn.TryGetComp<CompInventory>();
            if (inventory == null)
            {
                return;
            }
            int magSize = Mathf.Max(1, ammoUser.MagSizeOverride > 0 ? ammoUser.MagSizeOverride : ammoUser.MagSize);
            Thing spare = ThingMaker.MakeThing(ammo);
            if (spare == null)
            {
                return;
            }
            spare.stackCount = spareMagazines * magSize;
            if (!inventory.CanFitInInventory(spare, out int fits, false, false))
            {
                spare.Destroy();
                return;
            }
            if (fits < spare.stackCount)
            {
                // Trim to whole magazines so a partially fitting stack is still usable.
                spare.stackCount = fits - (fits % magSize);
            }
            if (spare.stackCount <= 0 || !inventory.container.TryAdd(spare, true))
            {
                if (!spare.Destroyed)
                {
                    spare.Destroy();
                }
                return;
            }
            inventory.UpdateInventory();
        }

        /// <summary>
        /// Same selection rule Combat Extended uses for generated pawns: filter on generateAllowChance, weight by it.
        /// generateCommonality is deliberately not used - it is 0 on most ammo defs.
        /// </summary>
        private static AmmoDef PickAmmo(CompAmmoUser ammoUser)
        {
            AmmoSetDef set = ammoUser.CurAmmoSet;
            if (set?.ammoTypes == null)
            {
                return ammoUser.CurrentAmmo;
            }
            var candidates = set.ammoTypes
                .Where(x => x?.ammo != null && x.ammo.alwaysHaulable && !x.ammo.menuHidden && x.ammo.generateAllowChance > 0f)
                .Select(x => x.ammo)
                .ToList();
            if (candidates.Count == 0)
            {
                return ammoUser.CurrentAmmo ?? set.ammoTypes.FirstOrDefault()?.ammo;
            }
            if (candidates.TryRandomElementByWeight(x => x.generateAllowChance, out AmmoDef result) && result != null)
            {
                return result;
            }
            return candidates.RandomElement();
        }

        private static int SpareMagazineCount(Pawn pawn)
        {
            LoadoutPropertiesExtension ext = pawn?.kindDef?.GetModExtension<LoadoutPropertiesExtension>();
            if (ext == null)
            {
                return 0;
            }
            return Mathf.Max(0, Mathf.RoundToInt(ext.primaryMagazineCount.RandomInRange));
        }
    }
}
