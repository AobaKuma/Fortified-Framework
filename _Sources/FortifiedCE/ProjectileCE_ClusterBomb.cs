using CombatExtended;
using CombatExtended.Utilities;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Fortified;

namespace FortifiedCE
{
    public class ProjectileCE_ClusterBomb : ProjectileCE
    {
        ModExtension_ClusterBomb Extension => def.GetModExtension<ModExtension_ClusterBomb>();
        public override void Tick()
        {
            base.Tick();
            //如果目前飛行距離與目標距離小於一個特定距離
            IntRange tDDistance = Extension.TDDistance;
            float a = Vector3.Distance(intendedTarget.CenterVector3, ExactPosition);
            float b = Vector3.Distance(origin, ExactPosition);
            if (a < tDDistance.min && b > Extension.safetyDistance)
            {
                if (this.GetComp<CompFragments>() != null)
                {
                    GetComp<CompFragments>().Throw(ExactPosition, base.Map, launcher);
                }
                if (Extension.doDismantlExplosion)
                {
                    GenExplosion.DoExplosion(ExactPosition.ToIntVec3(), Map, Extension.dismantlExplosionRadius, Extension.dismantlExplosionDam, (Thing)this, Extension.dismantlExplosionDamAmount, Extension.dismantlExplosionArmorPenetration, Extension.dismantlExplosionSound, (ThingDef)null, (ThingDef)null, (Thing)null, (ThingDef)null, 1f, 1, null,null,255,true, (ThingDef)null, 0f, 1, 0f, false, (float?)null, (List<Thing>)null);
                }
                Dismantling();
            }
        }
        private void GoodwillCorrection(List<IntVec3> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                List<Thing> thingList = cells[i].GetThingList(base.Map);
                foreach (Thing item in thingList)
                {
                    if (item is Pawn pawn && !pawn.Dead && pawn.Faction != null && !pawn.Faction.IsPlayer && !pawn.Faction.HostileTo(launcher.Faction))
                    {
                        FactionRelation factionRelation = pawn.Faction.RelationWith(launcher.Faction);
                        pawn.Faction.TryAffectGoodwillWith(launcher.Faction, -10 - factionRelation.baseGoodwill + pawn.Faction.GoodwillWith(launcher.Faction));
                    }
                }
            }
        }

        private void Dismantling()
        {
            //Vector3 vector = ExactPosition - new Vector3(origin.x, 0f, origin.y);
            //IntVec3 intVec = (ExactPosition + vector.normalized * 5).ToIntVec3();
            float baseOffsetAngle = Extension.forceMissingRange;
            ThingDef projectile = Extension.projectile;
            for (int i = 0; i < Extension.clusterBurstCount; i++)
            {
                ProjectileCE projectile2 = (ProjectileCE)GenSpawn.Spawn(projectile, ExactPosition.ToIntVec3(), Map);
                float errorRange = Extension.forceMissingRange;
                List<IntVec3> list = GenRadial.RadialCellsAround(new Vector3(Destination.x,0, Destination.y).ToIntVec3(), errorRange, useCenter: true).ToList();
                IntVec3 intVec2 = list.RandomElement();
                this.TryGetComp<CompFragments>()?.Throw(intVec2.ToVector3(), Map, this);
                // 世界砲擊落進來的子母彈沒有發射者與武器（launcher／equipmentDef 為 null）：CE 的 Launch 在高處會讀
                // launcher.Spawned，ThingMaker.MakeThing(null) 也會出錯；暫借子彈自己當 launcher，發射後再清回原值。
                Thing equipment = equipmentDef != null ? ThingMaker.MakeThing(equipmentDef) : null;
                projectile2.Launch(launcher ?? projectile2, new Vector2(ExactPosition.x, ExactPosition.z), shotAngle + Rand.Range(-baseOffsetAngle, baseOffsetAngle), shotAngle + Rand.Range(-baseOffsetAngle, baseOffsetAngle), Height, shotSpeed, equipment);
                if (launcher == null) { projectile2.launcher = null; }
                GoodwillCorrection(list);
            }
            Destroy();
        }
    }
}
