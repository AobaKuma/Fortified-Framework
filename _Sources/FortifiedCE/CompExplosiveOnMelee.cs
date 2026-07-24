using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using CombatExtended;
using System.Reflection;
using UnityEngine;

namespace FortifiedCE
{
    public class CompExplosiveOnMelee : ThingComp
    {
        Pawn Pawn => parent as Pawn;
        public override string CompInspectStringExtra()
        {
            return "FFF.ExplosiveOnDead".Translate();//機體在死亡時會引爆攜帶的可爆炸物。
        }
        public override void Notify_UsedVerb(Pawn pawn, Verb verb)
        {
            if (verb.IsMeleeAttack)
            {
                if (pawn.IsPlayerControlled && verb.CurrentTarget.TryGetPawn(out var p) && p.IsPlayerControlled) return;
                ExplodeEquipment();
                FireCarriedAmmo(verb.CurrentTarget);
                ExplodeInventory();
                if (detonated) parent.Kill(null, null);
            }
            base.Notify_UsedVerb(pawn, verb);
        }
        private bool detonated = false;
        protected void ExplodeEquipment()
        {
            if (Pawn.equipment == null || Pawn.equipment.Primary == null) return;

            if (Pawn.equipment.Primary.TryGetComp<CompExplosiveCE>(out var comp))
            {
                Detonate(comp);
            }
            else if (Pawn.equipment.Primary.TryGetComp<CompExplosive>(out var comp2))
            {
                Detonate(comp2);
            }
        }
        protected void ExplodeInventory()
        {
            if (Pawn.inventory == null || Pawn.inventory.innerContainer.NullOrEmpty()) return;
            List<Thing> tmpThings = new List<Thing>();
            foreach (var item in Pawn.inventory.innerContainer)
            {
                if (item.def is AmmoDef) continue;
                if (item.HasComp<CompExplosiveCE>() || item.HasComp<CompExplosive>())
                {
                    tmpThings.Add(item);
                }
            }

            if (tmpThings.NullOrEmpty()) return;
            foreach (var thing in tmpThings)
            {
                if(thing.TryGetComp<CompExplosiveCE>(out var comp))Detonate(comp);
                else if (thing.TryGetComp<CompExplosive>(out var comp2)) Detonate(comp2);
            }
            Pawn.inventory.DestroyAll();
        }

        protected void FireCarriedAmmo(LocalTargetInfo target)
        {
            if (Pawn.inventory == null || Pawn.inventory.innerContainer.NullOrEmpty()) return;

            List<Thing> ammoThings = new List<Thing>();
            foreach (var item in Pawn.inventory.innerContainer)
            {
                if (item.def is AmmoDef) ammoThings.Add(item);
                if (ammoThings.Count >= 10) break;
            }
            if (ammoThings.NullOrEmpty()) return;

            bool canFire = target.IsValid && parent.Map != null;
            foreach (var ammoThing in ammoThings)
            {
                bool fired = false;
                if (canFire)
                {
                    try
                    {
                        fired = LaunchAmmoProjectile(ammoThing.def as AmmoDef, target);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning($"CompExplosiveOnMelee: failed to launch projectile for {ammoThing}: {ex}");
                    }
                }

                if (fired)
                {
                    ammoThing.Destroy();
                }
                // Fallback: no matching CE projectile could be launched (e.g. ammo not linked
                // in any AmmoSetDef) — if it still carries its own explosive comp, detonate it
                // at the target's position so it still reads as "fired at the enemy" instead of
                // just vanishing under the wielder's own feet.
                else if (ammoThing.TryGetComp<CompExplosiveCE>(out var comp))
                {
                    Detonate(comp, canFire ? target.Cell : (IntVec3?)null);
                    ammoThing.Destroy();
                }
                else if (ammoThing.TryGetComp<CompExplosive>(out var comp2))
                {
                    Detonate(comp2, canFire ? target.Cell : (IntVec3?)null);
                    ammoThing.Destroy();
                }
            }
        }

        protected bool LaunchAmmoProjectile(AmmoDef ammoDef, LocalTargetInfo target)
        {
            ThingDef projectileDef = ammoDef?.AmmoSetDefs
                ?.Where(set => set.ammoTypes != null)
                .SelectMany(set => set.ammoTypes)
                .FirstOrDefault(link => link.ammo == ammoDef)?.projectile;
            if (!(projectileDef?.projectile is ProjectilePropertiesCE ceProps)) return false;

            Map map = parent.Map;

            // Indirect-fire ordnance (mortar shells etc.) has projectile.speed == 0 — CE derives
            // its actual launch speed from a range-based charge system meant for artillery arcs,
            // which doesn't apply at melee range. Detonate it directly on the target's cell instead
            // of attempting a ballistic Launch().
            if (ceProps.speed <= 0f || ceProps.flyOverhead)
            {
                DetonateProjectileAt(projectileDef, ceProps, target.Cell, map);
                return true;
            }

            IntVec3 originCell = parent.PositionHeld;
            ProjectileCE projectile = (ProjectileCE)GenSpawn.Spawn(projectileDef, originCell, map);
            projectile.intendedTarget = target;
            projectile.canTargetSelf = false;

            Vector2 originVec = new Vector2(originCell.x + 0.5f, originCell.z + 0.5f);
            Vector2 targetVec = new Vector2(target.Cell.x + 0.5f, target.Cell.z + 0.5f);
            // ProjectileCE.Launch() turns shotRotation into a direction via
            // RotatedBy(Vector2.up, shotRotation) = (-sin(θ), cos(θ)), i.e. 0°=north, 90°=west,
            // 180°=south, 270°=east (counter-clockwise from north).
            float shotRotation = Mathf.Atan2(-(targetVec.x - originVec.x), targetVec.y - originVec.y) * Mathf.Rad2Deg;

            // CE_Utility.MaxProjectileRange (the formula behind flight distance) collapses to zero
            // whenever shotAngle is 0 and shotHeight is ~0, regardless of speed. Use CE's own
            // ballistic solver (the same one Verb_LaunchProjectileCE uses) to get a real
            // shotAngle/shotHeight pair that actually covers the distance to the target.
            const float shotHeight = 0.5f;
            Vector3 source3 = new Vector3(originVec.x, shotHeight, originVec.y);
            Vector3 target3 = new Vector3(targetVec.x, 0f, targetVec.y);
            float shotAngle = ceProps.TrajectoryWorker.ShotAngle(ceProps, source3, target3, ceProps.speed);

            projectile.Launch(Pawn, originVec, shotAngle, shotRotation, shotHeight, ceProps.speed, Pawn.equipment?.Primary);
            return true;
        }

        protected void DetonateProjectileAt(ThingDef projectileDef, ProjectilePropertiesCE props, IntVec3 cell, Map map)
        {
            detonated = true;
            int damAmount = props.GetDamageAmount(1f, null);
            float armorPenetration = props.GetArmorPenetration(null);
            GenExplosionCE.DoExplosion(cell, map, props.explosionRadius, props.damageDef, this.parent,
                damAmount, armorPenetration, props.soundExplode,
                null, projectileDef, null,
                props.postExplosionSpawnThingDef, props.postExplosionSpawnChance, props.postExplosionSpawnThingCount,
                props.postExplosionGasType, null, 255,
                props.applyDamageToExplosionCellsNeighbors,
                props.preExplosionSpawnThingDef, props.preExplosionSpawnChance, props.preExplosionSpawnThingCount,
                props.explosionChanceToStartFire, props.explosionDamageFalloff,
                null, null, null,
                true, 1f, 0f, true,
                props.postExplosionSpawnThingDefWater, props.screenShakeFactor,
                null, null,
                props.postExplosionSpawnSingleThingDef, props.preExplosionSpawnSingleThingDef);
        }

        public static CompProperties_ExplosiveCE GetProps(CompExplosiveCE instance)
        {
            if (instance == null) return null;
            PropertyInfo propInfo = typeof(CompExplosiveCE).GetProperty("Props", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (propInfo != null)
            {
                return propInfo.GetValue(instance) as CompProperties_ExplosiveCE;
            }
            return null;
        }
        protected virtual void Detonate(CompExplosive comp, IntVec3? explodeAt = null)
        {
            detonated = true;
            var compProperties_Explosive = comp.Props as CompProperties_Explosive;
            var Props = comp.Props;
            var map = parent.Map;

            if (comp.parent.def.projectileWhenLoaded != null)
            {
                ThingDef i = comp.parent.def.projectileWhenLoaded;
                if (i.HasComp<CompExplosive>())
                {
                    compProperties_Explosive = i.GetCompProperties<CompProperties_Explosive>();
                    Props = i.GetCompProperties<CompProperties_Explosive>();
                }
            }

            IntVec3 positionHeld = explodeAt ?? parent.PositionHeld;
            DamageDef explosiveDamageType = compProperties_Explosive.explosiveDamageType;
            int damageAmountBase = compProperties_Explosive.damageAmountBase;
            float armorPenetrationBase = compProperties_Explosive.armorPenetrationBase;
            SoundDef explosionSound = compProperties_Explosive.explosionSound;
            ThingDef postExplosionSpawnThingDef = compProperties_Explosive.postExplosionSpawnThingDef;
            float postExplosionSpawnChance = compProperties_Explosive.postExplosionSpawnChance;
            int postExplosionSpawnThingCount = compProperties_Explosive.postExplosionSpawnThingCount;
            GasType? postExplosionGasType = Props.postExplosionGasType;
            float? postExplosionGasRadiusOverride = Props.postExplosionGasRadiusOverride;
            int postExplosionGasAmount = Props.postExplosionGasAmount;
            bool applyDamageToExplosionCellsNeighbors = compProperties_Explosive.applyDamageToExplosionCellsNeighbors;
            ThingDef preExplosionSpawnThingDef = compProperties_Explosive.preExplosionSpawnThingDef;
            float preExplosionSpawnChance = compProperties_Explosive.preExplosionSpawnChance;
            int preExplosionSpawnThingCount = compProperties_Explosive.preExplosionSpawnThingCount;
            float chanceToStartFire = compProperties_Explosive.chanceToStartFire;
            bool damageFalloff = compProperties_Explosive.damageFalloff;
            List<Thing> ignoredThings = null;
            bool doVisualEffects = compProperties_Explosive.doVisualEffects;
            bool doSoundEffects = compProperties_Explosive.doSoundEffects;
            float propagationSpeed = compProperties_Explosive.propagationSpeed;
            ThingDef preExplosionSpawnSingleThingDef = compProperties_Explosive.preExplosionSpawnSingleThingDef;
            ThingDef postExplosionSpawnSingleThingDef = compProperties_Explosive.postExplosionSpawnSingleThingDef;
            GenExplosion.DoExplosion(positionHeld, map, comp.ExplosiveRadius(), explosiveDamageType, this.parent, damageAmountBase, armorPenetrationBase, explosionSound, null, null, null, postExplosionSpawnThingDef, postExplosionSpawnChance, postExplosionSpawnThingCount, postExplosionGasType, postExplosionGasRadiusOverride, postExplosionGasAmount, applyDamageToExplosionCellsNeighbors, preExplosionSpawnThingDef, preExplosionSpawnChance, preExplosionSpawnThingCount, chanceToStartFire, damageFalloff, null, ignoredThings, null, doVisualEffects, propagationSpeed, 0f, doSoundEffects, null, 1f, null, null, postExplosionSpawnSingleThingDef, preExplosionSpawnSingleThingDef);
        }
        protected virtual void Detonate(CompExplosiveCE comp, IntVec3? explodeAt = null)
        {
            detonated = true;
            var compProperties_Explosive = GetProps(comp);
            var Props = GetProps(comp);
            var map = parent.Map;

            if (comp.parent.def.projectileWhenLoaded != null)
            {
                ThingDef i = comp.parent.def.projectileWhenLoaded;
                if (i.HasComp<CompExplosiveCE>())
                {
                    compProperties_Explosive = i.GetCompProperties<CompProperties_ExplosiveCE>();
                    Props = i.GetCompProperties<CompProperties_ExplosiveCE>();
                }
            }
            if (Props == null)
            {
                Log.Error($"CompExplosiveOnMelee: {comp.parent} has no CompProperties_ExplosiveCE defined.");
                return;
            }

            IntVec3 positionHeld = explodeAt ?? parent.PositionHeld;
            DamageDef explosiveDamageType = compProperties_Explosive.explosiveDamageType;
            int damageAmountBase = (int)compProperties_Explosive.damageAmountBase;
            float armorPenetrationBase = compProperties_Explosive.GetExplosionArmorPenetration();
            SoundDef explosionSound = compProperties_Explosive.explosionSound;
            ThingDef postExplosionSpawnThingDef = compProperties_Explosive.postExplosionSpawnThingDef;
            float postExplosionSpawnChance = compProperties_Explosive.postExplosionSpawnChance;
            int postExplosionSpawnThingCount = compProperties_Explosive.postExplosionSpawnThingCount;
            GasType? postExplosionGasType = Props.postExplosionGasType;
            float? postExplosionGasRadiusOverride = Props.postExplosionGasRadiusOverride;
            int postExplosionGasAmount = Props.postExplosionGasAmount;
            bool applyDamageToExplosionCellsNeighbors = compProperties_Explosive.applyDamageToExplosionCellsNeighbors;
            ThingDef preExplosionSpawnThingDef = compProperties_Explosive.preExplosionSpawnThingDef;
            float preExplosionSpawnChance = compProperties_Explosive.preExplosionSpawnChance;
            int preExplosionSpawnThingCount = compProperties_Explosive.preExplosionSpawnThingCount;
            float chanceToStartFire = compProperties_Explosive.chanceToStartFire;
            bool damageFalloff = compProperties_Explosive.damageFalloff;
            List<Thing> ignoredThings = null;
            bool doVisualEffects = explodeAt.HasValue;
            bool doSoundEffects = true;
            float propagationSpeed = compProperties_Explosive.fragSpeedFactor;
            ThingDef preExplosionSpawnSingleThingDef = compProperties_Explosive.preExplosionSpawnThingDef;
            ThingDef postExplosionSpawnSingleThingDef = compProperties_Explosive.postExplosionSpawnThingDef;

            GenExplosionCE.DoExplosion(positionHeld, map, Props.explosiveRadius, explosiveDamageType, this.parent, damageAmountBase, armorPenetrationBase, explosionSound, null, null,null, postExplosionSpawnThingDef, postExplosionSpawnChance, postExplosionSpawnThingCount, postExplosionGasType, postExplosionGasRadiusOverride, postExplosionGasAmount, applyDamageToExplosionCellsNeighbors, preExplosionSpawnThingDef, preExplosionSpawnChance, preExplosionSpawnThingCount, chanceToStartFire, damageFalloff, null, ignoredThings, null, doVisualEffects, propagationSpeed, 0f, doSoundEffects, null, 1f, null, null, postExplosionSpawnSingleThingDef, preExplosionSpawnSingleThingDef);
        }
    }
}
