using RimWorld;
using CombatExtended;
using Verse;
using Fortified;

namespace FortifiedCE
{
    public class ProjectileCE_ExplosiveByComps : ProjectileCE_Explosive
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
        public override void Tick()
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
                            GenExplosionCE.DoExplosion(
                                Position,
                                Map,
                                explosion.radius,
                                explosion.damamgeDef,
                                launcher,
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
                                postExplosionGasRadiusOverride: null,
                                postExplosionGasAmount: 255,
                                false,
                                explosion.preExplosionSpawnThingDef,
                                explosion.preExplosionSpawnChance,
                                explosion.preExplosionSpawnThingCount,
                                explosion.chanceToStartFire,
                                false,
                                origin.AngleTo(Destination),
                                null,
                                null,
                                true,
                                def.projectile.damageDef.expolosionPropagationSpeed,
                                0f,
                                true,
                                explosion.postExplosionSpawnThingDefWater,
                                0);
                        }
                    }
                }
            }
            base.Tick();
        }
        public override void Impact(Thing hitThing)
        {
            ticksToDetonation_ForComps = def.projectile.explosionDelay;
            if (compExpolsionWithEvents != null)
            {
                foreach (Condition condition in compExpolsionWithEvents.conditions)
                {
                    TryStartCondition(condition);
                }
            }
            base.Impact(hitThing);
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
