using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>
    /// Turrets never read <see cref="Verb.WarmupTime"/>: Building_TurretGun takes its burst warmup from
    /// <c>def.building.turretBurstWarmupTime</c>. Scale that warmup by the loaded ammo's warmUpFactor so
    /// <see cref="AmmoOption.warmUpFactor"/> works on turret guns the same way it does on pawn weapons.
    /// </summary>
    [HarmonyPatch(typeof(Building_TurretGun), nameof(Building_TurretGun.TryStartShootSomething))]
    public static class Patch_BuildingTurretGun_AmmoWarmup
    {
        private static readonly AccessTools.FieldRef<Building_TurretGun, int> BurstWarmupTicksLeft =
            AccessTools.FieldRefAccess<Building_TurretGun, int>("burstWarmupTicksLeft");

        public static void Prefix(Building_TurretGun __instance, out int __state)
        {
            __state = BurstWarmupTicksLeft(__instance);
        }

        public static void Postfix(Building_TurretGun __instance, int __state)
        {
            int ticks = BurstWarmupTicksLeft(__instance);
            // Only touch a warmup this call just started; 1 is vanilla's "begin next tick" placeholder.
            if (ticks <= 1 || ticks == __state) return;

            float factor = __instance.gun?.TryGetComp<CompAmmoSwitch>()?.CurrentAmmo?.warmUpFactor ?? 1f;
            if (Mathf.Approximately(factor, 1f)) return;

            BurstWarmupTicksLeft(__instance) = Mathf.Max(1, Mathf.RoundToInt(ticks * factor));
        }
    }
}
