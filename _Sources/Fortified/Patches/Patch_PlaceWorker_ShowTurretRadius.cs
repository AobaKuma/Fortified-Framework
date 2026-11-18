using HarmonyLib;
using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// Vanilla PlaceWorker_ShowTurretRadius looks for a verb whose class is exactly Verb_Shoot (or a
    /// Verb_Spray) and dereferences the result unchecked. Any turret gun using a Verb_Shoot subclass or a
    /// plain Verb_LaunchProjectile subclass — e.g. FFF's Verb_Shoot_AmmoSwitch /
    /// Verb_LaunchProjectile_AmmoSwitch — throws a NullReferenceException every frame while the blueprint
    /// is being placed. Replace it with a null-safe lookup that keeps vanilla's choice first.
    /// </summary>
    [HarmonyPatch(typeof(PlaceWorker_ShowTurretRadius), nameof(PlaceWorker_ShowTurretRadius.AllowsPlacing))]
    public static class Patch_PlaceWorker_ShowTurretRadius
    {
        public static bool Prefix(BuildableDef checkingDef, IntVec3 loc, ref AcceptanceReport __result)
        {
            __result = true;
            ThingDef gunDef = (checkingDef as ThingDef)?.building?.turretGunDef;
            if (gunDef?.Verbs == null) return false;

            VerbProperties verb =
                gunDef.Verbs.Find(v => v.verbClass == typeof(Verb_Shoot) || (v.verbClass != null && typeof(Verb_Spray).IsAssignableFrom(v.verbClass)))
                ?? gunDef.Verbs.Find(v => v.verbClass != null && typeof(Verb_LaunchProjectile).IsAssignableFrom(v.verbClass))
                ?? gunDef.Verbs.Find(v => v.range > 0f);
            if (verb == null) return false;

            // A freshly built ammo-switch turret starts on its default ammo; show that ammo's reach.
            float range = verb.range;
            CompProperties_AmmoSwitch ammoProps = gunDef.GetCompProperties<CompProperties_AmmoSwitch>();
            if (ammoProps != null && !ammoProps.ammos.NullOrEmpty())
            {
                int idx = UnityEngine.Mathf.Clamp(ammoProps.defaultIndex, ammoProps.MinSelectableIndex, ammoProps.ammos.Count - 1);
                if (idx >= 0 && ammoProps.ammos[idx] != null) range *= ammoProps.ammos[idx].rangeFactor;
            }

            if (range > 0f) GenDraw.DrawRadiusRing(loc, range);
            if (verb.minRange > 0f) GenDraw.DrawRadiusRing(loc, verb.minRange);
            return false;
        }
    }
}
