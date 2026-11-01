using Verse;
using RimWorld;
using System;
using UnityEngine;
using System.Collections.Generic;

namespace Fortified
{
    public class Verb_MeleeSweep : Verb_MeleeAttackDamage
    {
        private Comp_MeleeSweep cachedSweepComp;
        // 武器上的掃擊優先；拳腳等天生武器沒有 EquipmentSource，改讀施放者（種族）身上的掃擊。
        private Comp_MeleeSweep SweepComp => cachedSweepComp ??= EquipmentSource?.GetComp<Comp_MeleeSweep>() ?? CasterPawn?.GetComp<Comp_MeleeSweep>();
        private EffecterDef ExplosionEffect => SweepComp.Props.explosionEffect;
        private DamageDef DamageDef => SweepComp.Props.damageDef;
        private float Radius => SweepComp.Props.radius;
        private float Angle => SweepComp.Props.angle;
        private float Damage => SweepComp.Props.damage;
        private float ArmorPenetration => SweepComp.Props.armorPenetration;
        public override bool TryStartCastOn(LocalTargetInfo castTarg, LocalTargetInfo destTarg, bool surpriseAttack = false, bool canHitNonTargetPawns = true, bool preventFriendlyFire = false, bool nonInterruptingSelfCast = false)
        {
            return base.TryStartCastOn(castTarg, destTarg, surpriseAttack, canHitNonTargetPawns, preventFriendlyFire, nonInterruptingSelfCast);
        }
        protected override DamageWorker.DamageResult ApplyMeleeDamageToTarget(LocalTargetInfo target)
        {
            // 武器與種族都沒有掃擊設定時，照一般近戰結算。
            if (SweepComp == null)
            {
                return base.ApplyMeleeDamageToTarget(target);
            }
            DamageWorker.DamageResult result = new DamageWorker.DamageResult();
            Pawn casterPawn = CasterPawn;
            if (casterPawn != null && !casterPawn.Downed && CasterIsPawn)
            {
                DoSweep(target, out result);
            }
            return result;
        }
        public override bool IsMeleeAttack => true;
        public void DoSweep(LocalTargetInfo target, out DamageWorker.DamageResult result)
        {
            if (ExplosionEffect != null)
            {
                Effecter effecter = ExplosionEffect.Spawn();
                effecter.Trigger(new TargetInfo(target.Cell, target.Thing.Map), new TargetInfo(target.Thing.PositionHeld, target.Thing.Map));
                effecter.Cleanup();
            }

            IntVec3 pos = CasterPawn.Position;
            float direction = Mathf.Atan2(-(target.Cell.z - pos.z), target.Cell.x - pos.x) * 57.29578f;

            target.Thing.TakeDamage(new DamageInfo(DamageDefOf.Stun, 20f * Rand.Range(1f, 3f)));

            //武器品質；天生武器沒有品質，傷害來源記為施放者的種族（同原版近戰）
            var q = QualityCategory.Normal;
            EquipmentSource?.TryGetQuality(out q);
            ThingDef weaponDef = EquipmentSource?.def ?? CasterPawn.def;

            result = target.Thing.TakeDamage(new DamageInfo(DamageDef, Damage, armorPenetration: ArmorPenetration, weapon: weaponDef, instigator: CasterPawn, weaponQuality: q));
            // 武器在這一擊中損毀，或施放者已離開地圖時，不再擴散
            if ((EquipmentSource == null || !EquipmentSource.Destroyed) && CasterPawn.Spawned)
            {
                // 友伤判断
                List<Thing> AvoidThings = new List<Thing>();
                AvoidThings.Add(CasterPawn);
                Faction casterFaction = CasterPawn.Faction;
                ThingGrid thingGrid = CasterPawn.MapHeld.thingGrid;
                foreach (IntVec3 cell in CasterPawn.OccupiedRect().ExpandedBy((int)Radius))
                {
                    List<Thing> things = thingGrid.ThingsListAt(cell);
                    for (int idx = 0; idx < things.Count; idx++)
                    {
                        Thing v = things[idx];
                        if (v is Pawn pawn && (pawn.DeadOrDowned || (casterFaction != null && pawn.Faction != null && !pawn.Faction.HostileTo(casterFaction))))
                        {
                            AvoidThings.Add(v);
                        }
                        else if (v.def.category == ThingCategory.Item)
                        {
                            AvoidThings.Add(v);
                        }
                    }
                }

                if (target.Pawn == null)
                {
                    pos += Rot4.FromAngleFlat(direction).Opposite.FacingCell; //面對非Pawn目標的範圍會更大。
                }
                GenExplosion.DoExplosion(pos, CasterPawn.Map, Radius, DamageDef, CasterPawn, (int)Damage, armorPenetration: ArmorPenetration, screenShakeFactor: 0, doVisualEffects: false, ignoredThings: AvoidThings, explosionSound: SoundDefOf.Pawn_Melee_Punch_Miss, affectedAngle: new FloatRange(direction - (Angle / 2f) - 10, direction + (Angle / 2f) + 10), direction: direction, propagationSpeed: 0.6f);
            }
        }
    }
    public class Comp_MeleeSweep : ThingComp
    {
        public CompProperties_MeleeSweep Props => (CompProperties_MeleeSweep)props;
    }
    public class CompProperties_MeleeSweep : CompProperties
    {
        public DamageDef damageDef;

        public float radius = 3f;
        public float angle = 90f;
        public float damage = 50f;

        public float armorPenetration = 0.5f;

        public EffecterDef explosionEffect;

        public CompProperties_MeleeSweep()
        {
            compClass = typeof(Comp_MeleeSweep);
        }
    }
}
