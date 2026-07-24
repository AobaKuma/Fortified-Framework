using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace Fortified
{
    // 机械体涂装工作分配
    public class WorkGiver_PaintMech : WorkGiver_Scanner
    {
        private static ThingDef cachedDyeDef;

        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        // 检查是否有工作
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t is not Pawn mech || !mech.Spawned) return false;

            var comp = mech.TryGetComp<CompPaintable>();
            if (comp == null || !comp.activePaintRequest) return false;

            if (!pawn.CanReserve(mech, 1, -1, null, forced)) return false;

            int needed = Mathf.CeilToInt(mech.BodySize * 4f);
            if (!FindDye(pawn, needed, out _))
            {
                JobFailReason.Is("FFF_NoDye".Translate());
                return false;
            }

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t is not Pawn mech) return null;
            int needed = Mathf.CeilToInt(mech.BodySize * 4f);
            if (FindDye(pawn, needed, out var dye))
            {
                var job = JobMaker.MakeJob(FFF_JobDefOf.FFF_PaintMech, t, dye);
                job.count = needed;
                return job;
            }
            return null;
        }

        private bool FindDye(Pawn pawn, int needed, out Thing dye)
        {
            if (cachedDyeDef == null)
            {
                cachedDyeDef = DefDatabase<ThingDef>.GetNamed("Dye", false);
                if (cachedDyeDef == null) { dye = null; return false; }
            }

            var allDyes = pawn.Map.listerThings.ThingsOfDef(cachedDyeDef);
            dye = GenClosest.ClosestThing_Global_Reachable(
                pawn.Position,
                pawn.Map,
                allDyes,
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                (t) => !t.IsForbidden(pawn) && pawn.CanReserve(t) && t.stackCount >= needed
            );

            return dye != null;
        }
    }

    // 建筑涂装工作分配
    public class WorkGiver_PaintBuilding : WorkGiver_Scanner
    {
        private static ThingDef cachedDyeDef;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (pawn?.Map?.listerBuildings == null) yield break;

            List<Building> buildings = pawn.Map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building b = buildings[i];
                var comp = b.TryGetComp<CompPaintable>();
                if (comp != null && comp.activePaintRequest)
                {
                    yield return b;
                }
            }
        }

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t is not Building building || !building.Spawned) return false;

            var comp = building.TryGetComp<CompPaintable>();
            if (comp == null || !comp.activePaintRequest) return false;

            if (!pawn.CanReserve(building, 1, -1, null, forced)) return false;

            int needed = Mathf.FloorToInt(t.def.size.x * t.def.size.z * 1.5f);
            if (!FindDye(pawn, needed, out _))
            {
                JobFailReason.Is("FFF_NoDye".Translate());
                return false;
            }

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t is not Building building) return null;

            int needed = Mathf.FloorToInt(t.def.size.x * t.def.size.z * 1.5f);
            if (FindDye(pawn, needed, out var dye))
            {
                var job = JobMaker.MakeJob(FFF_JobDefOf.FFF_PaintMech, t, dye);
                job.count = needed;
                return job;
            }
            return null;
        }

        private bool FindDye(Pawn pawn, int needed, out Thing dye)
        {
            if (cachedDyeDef == null)
            {
                cachedDyeDef = DefDatabase<ThingDef>.GetNamed("Dye", false);
                if (cachedDyeDef == null) { dye = null; return false; }
            }

            var allDyes = pawn.Map.listerThings.ThingsOfDef(cachedDyeDef);
            dye = GenClosest.ClosestThing_Global_Reachable(
                pawn.Position,
                pawn.Map,
                allDyes,
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                (t) => !t.IsForbidden(pawn) && pawn.CanReserve(t) && t.stackCount >= needed
            );

            return dye != null;
        }
    }
}
