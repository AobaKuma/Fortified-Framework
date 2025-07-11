﻿using RimWorld;
using Verse;

namespace Fortified
{
    public class Projectile_ExplosiveByComps : Projectile_Explosive
    {
        public int ticksToDetonation_ForComps = -1;
        public ModExtension_CompositeExplosion compCompositeExplosion;
        public ModExtension_ExpolsionWithConditions compExpolsionWithEvents;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            compCompositeExplosion = def.GetModExtension<ModExtension_CompositeExplosion>();
            compExpolsionWithEvents = def.GetModExtension<ModExtension_ExpolsionWithConditions>();
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksToDetonation_ForComps, "ticksToDetonation_ForComps", -1);
        }
        protected override void Tick()
        {
            if (ticksToDetonation_ForComps > 0)
            {
                ticksToDetonation_ForComps--;
                if (compCompositeExplosion != null)
                {
                    foreach (CompositeExplosion explosion in compCompositeExplosion.compositeExplosions)
                    {
                        if (explosion.countdown == ticksToDetonation_ForComps)
                        {
                            GenExplosion.DoExplosion(
                                Position,
                                Map,
                                explosion.radius,
                                explosion.damamgeDef,
                                Launcher,
                                explosion.amount,

                                explosion.armorPenetration ?? -1,
                                explosion.explosionSound,
                                equipmentDef,
                                def,
                                intendedTarget.Thing,

                                explosion.postExplosionSpawnThingDef,
                                explosion.postExplosionSpawnChance,
                                explosion.postExplosionSpawnThingCount,
                                explosion.postExplosionGasType,
                                null,
                                0,
                                false,
                                explosion.preExplosionSpawnThingDef,
                                explosion.preExplosionSpawnChance,
                                explosion.preExplosionSpawnThingCount,
                                explosion.chanceToStartFire,
                                damageFalloff: explosion.damageFalloff,
                                origin.AngleToFlat(destination),
                                null,
                                explosion.affectedAngle,
                                true,
                                def.projectile.damageDef.expolosionPropagationSpeed,
                                0f,
                                true
                                );
                        }
                    }
                }
            }
            base.Tick();
        }
        protected override void Impact(Thing hitThing, bool blockedByShield)
        {
            ticksToDetonation_ForComps = def.projectile.explosionDelay;
            if (compExpolsionWithEvents != null)
            {
                foreach (Condition condition in compExpolsionWithEvents.conditions)
                {
                    TryStartCondition(condition);
                }
            }
            base.Impact(hitThing, blockedByShield);
        }
        private void TryStartCondition(Condition condition)
        {
            if (Rand.Range(1, 101) > condition.percent)
            {
                return;
            }
            foreach (GameCondition x in Map.gameConditionManager.ActiveConditions)
            {
                if (x.def == condition.conditionDef)
                    return;
            }
            GameCondition gameCondition = GameConditionMaker.MakeCondition(condition.conditionDef, condition.duration.RandomInRange);
            Map.gameConditionManager.RegisterCondition(gameCondition);
        }
    }
}
