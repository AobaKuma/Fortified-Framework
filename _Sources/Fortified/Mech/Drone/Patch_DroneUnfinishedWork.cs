using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Fortified
{
    // Drone recall destroys the pawn. Preserve enough ownership information to
    // resume its work even after a save/load has cleared the destroyed reference.
    [HarmonyPatch(typeof(UnfinishedThing), nameof(UnfinishedThing.ExposeData))]
    internal static class Patch_DroneUnfinishedOwnership
    {
        internal sealed class Ownership
        {
            public Faction faction;
        }

        private static readonly ConditionalWeakTable<UnfinishedThing, Ownership> owners = new();

        internal static Faction DroneFaction(UnfinishedThing thing)
        {
            Ownership owner = owners.GetValue(thing, _ => new Ownership());
            if (thing.Creator != null)
            {
                owner.faction = thing.Creator.TryGetComp<CompDrone>() != null
                    ? thing.Creator.Faction : null;
            }
            return owner.faction;
        }

        private static void Postfix(UnfinishedThing __instance)
        {
            Ownership owner = owners.GetValue(__instance, _ => new Ownership());
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                DroneFaction(__instance);
            }
            Scribe_References.Look(ref owner.faction, "FFF_droneCreatorFaction");
        }
    }

    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    internal static class Patch_DroneUnfinishedWork
    {
        internal sealed class Transfer
        {
            public UnfinishedThing thing;
            public Pawn creator;
            public string creatorName;
            public bool restored;
        }

        private static void Prefix(Pawn pawn, Thing thing, out List<Transfer> __state)
        {
            __state = null;
            if (pawn?.Faction == null || pawn.Map == null || thing is not IBillGiver giver)
                return;

            List<Thing> unfinished = new();
            HashSet<ThingDef> seen = new();
            for (int j = 0; j < giver.BillStack.Count; j++)
            {
                ThingDef unfinishedDef = giver.BillStack[j].recipe.unfinishedThingDef;
                if (unfinishedDef != null && seen.Add(unfinishedDef))
                    unfinished.AddRange(pawn.Map.listerThings.ThingsOfDef(unfinishedDef));
            }
            for (int i = 0; i < unfinished.Count; i++)
            {
                if (unfinished[i] is not UnfinishedThing uft || uft.IsForbidden(pawn)
                    || Patch_DroneUnfinishedOwnership.DroneFaction(uft) != pawn.Faction)
                    continue;

                Pawn creator = uft.Creator;
                // An active drone keeps exclusive ownership, including while it
                // walks home. Transfer only once that pawn can no longer work.
                if (creator != null && !creator.Destroyed && !creator.Dead)
                    continue;
                if (uft.BoundBill != null && uft.BoundBill.billStack?.billGiver != giver)
                    continue;

                bool matchingBill = false;
                for (int j = 0; j < giver.BillStack.Count; j++)
                {
                    if (giver.BillStack[j] is Bill_ProductionWithUft bill && bill.recipe == uft.Recipe)
                    {
                        matchingBill = true;
                        break;
                    }
                }
                if (!matchingBill || !pawn.CanReserveAndReach(uft, PathEndMode.Touch, Danger.Deadly))
                    continue;

                __state ??= new List<Transfer>();
                __state.Add(new Transfer { thing = uft, creator = creator, creatorName = creatorNameField(uft) });
                uft.Creator = pawn;
            }
        }

        private static void Postfix(Job __result, List<Transfer> __state)
        {
            Restore(__state, __result);
        }

        private static Exception Finalizer(Exception __exception, List<Transfer> __state)
        {
            if (__exception != null)
                Restore(__state, null);
            return __exception;
        }

        private static void Restore(List<Transfer> transfers, Job job)
        {
            if (transfers == null) return;
            foreach (Transfer transfer in transfers)
            {
                if (transfer.restored) continue;
                transfer.restored = true;
                // Vanilla checks bill restrictions, skills, ingredients and
                // reservations. Commit ownership only for its chosen workpiece.
                if (job?.def == JobDefOf.DoBill && job.targetQueueB != null
                    && job.targetQueueB.Contains(transfer.thing))
                    continue;
                if (transfer.creator != null)
                    transfer.thing.Creator = transfer.creator;
                else
                    creatorField(transfer.thing) = null;
                creatorNameField(transfer.thing) = transfer.creatorName;
            }
        }

        // The public setter rejects null; orphaned references must be restored
        // exactly when vanilla did not select this unfinished item.
        private static readonly AccessTools.FieldRef<UnfinishedThing, string> creatorNameField =
            AccessTools.FieldRefAccess<UnfinishedThing, string>("creatorName");
        private static readonly AccessTools.FieldRef<UnfinishedThing, Pawn> creatorField =
            AccessTools.FieldRefAccess<UnfinishedThing, Pawn>("creatorInt");
    }
}

