using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Fortified
{
    public class MapComponent_ModificationIndex : MapComponent
    {
        private readonly Dictionary<ThingDef, HashSet<Thing>> stacksByDef = new Dictionary<ThingDef, HashSet<Thing>>();
        private readonly Dictionary<Thing, IntVec3> cachedPositions = new Dictionary<Thing, IntVec3>();

        public MapComponent_ModificationIndex(Map map) : base(map)
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Rebuild();
        }

        public void Rebuild()
        {
            stacksByDef.Clear();
            cachedPositions.Clear();
            List<Thing> things = map?.listerThings?.AllThings;
            if (things == null) return;
            for (int i = 0; i < things.Count; i++) Register(things[i]);
        }

        public void Register(Thing thing)
        {
            // Index every spawned item stack.  Besides native FFF modifications, this
            // also covers extension-defined modification items without requiring the
            // extension to register Defs or add any addressing logic of its own.
            if (!ShouldIndex(thing) || !thing.Spawned || thing.Map != map) return;
            if (!stacksByDef.TryGetValue(thing.def, out HashSet<Thing> stacks))
            {
                stacks = new HashSet<Thing>();
                stacksByDef.Add(thing.def, stacks);
            }
            stacks.Add(thing);
            cachedPositions[thing] = thing.Position;
        }

        public void Deregister(Thing thing)
        {
            if (thing == null) return;
            if (stacksByDef.TryGetValue(thing.def, out HashSet<Thing> stacks))
            {
                stacks.Remove(thing);
                if (stacks.Count == 0) stacksByDef.Remove(thing.def);
            }
            cachedPositions.Remove(thing);
        }

        public List<Thing> GetCandidates(ThingDef def, Pawn actor, IntVec3 origin, bool requireReachableAndReservable)
        {
            List<Thing> result = new List<Thing>();
            if (def == null || !stacksByDef.TryGetValue(def, out HashSet<Thing> stacks)) return result;
            List<Thing> invalid = null;
            foreach (Thing thing in stacks)
            {
                if (!IsLiveStack(thing, def))
                {
                    if (invalid == null) invalid = new List<Thing>();
                    invalid.Add(thing);
                    continue;
                }
                cachedPositions[thing] = thing.Position;
                if (actor != null)
                {
                    if (thing.IsForbidden(actor)) continue;
                    if (requireReachableAndReservable && (!actor.CanReach(thing, PathEndMode.Touch, Danger.Deadly) || !actor.CanReserve(thing))) continue;
                }
                result.Add(thing);
            }
            if (invalid != null)
            {
                for (int i = 0; i < invalid.Count; i++) Deregister(invalid[i]);
            }
            result.Sort((left, right) => DistanceSquared(left, origin).CompareTo(DistanceSquared(right, origin)));
            return result;
        }

        public int CountAvailable(ThingDef def, Pawn actor)
        {
            List<Thing> candidates = GetCandidates(def, actor, actor?.Position ?? IntVec3.Invalid, true);
            int count = 0;
            for (int i = 0; i < candidates.Count; i++) count += candidates[i].stackCount;
            return count;
        }

        private bool IsLiveStack(Thing thing, ThingDef def)
        {
            return thing != null && !thing.Destroyed && thing.Spawned && thing.Map == map && thing.def == def && thing.stackCount > 0;
        }

        private static bool ShouldIndex(Thing thing)
        {
            return thing?.def?.category == ThingCategory.Item;
        }

        private int DistanceSquared(Thing thing, IntVec3 origin)
        {
            if (thing == null || !origin.IsValid) return 0;
            IntVec3 position = cachedPositions.TryGetValue(thing, out IntVec3 cached) ? cached : thing.Position;
            return (position - origin).LengthHorizontalSquared;
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup), new[] { typeof(Map), typeof(bool) })]
    public static class Patch_ModificationIndex_ThingSpawnSetup
    {
        public static void Postfix(Thing __instance, Map map)
        {
            map?.GetComponent<MapComponent_ModificationIndex>()?.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn), new[] { typeof(DestroyMode) })]
    public static class Patch_ModificationIndex_ThingDeSpawn
    {
        public static void Prefix(Thing __instance)
        {
            __instance?.Map?.GetComponent<MapComponent_ModificationIndex>()?.Deregister(__instance);
        }
    }
}
