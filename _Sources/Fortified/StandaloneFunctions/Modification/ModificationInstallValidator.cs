using System.Collections.Generic;
using Verse;

namespace Fortified
{
    public static class ModificationInstallValidator
    {
        public static bool TryFindInstallPart(
            Pawn pawn,
            ThingDef itemDef,
            IReadOnlyList<MechModificationQueueEntry> queue,
            out BodyPartRecord part,
            out string reason,
            bool checkInventory = true)
        {
            part = null;
            reason = null;
            ModificationProfile profile = ModificationProfileDatabase.Get(itemDef);
            List<BodyPartRecord> parts = pawn?.RaceProps?.body?.AllParts;
            if (profile == null || parts == null)
            {
                reason = "FFF.MechModification.InvalidModification".Translate();
                return false;
            }
            if (!profile.TargetsBodyPart)
            {
                part = pawn.RaceProps.body.corePart;
                return CanInstall(pawn, itemDef, part, queue, out reason, checkInventory);
            }
            for (int i = 0; i < parts.Count; i++)
            {
                if (CanInstall(pawn, itemDef, parts[i], queue, out string candidateReason, checkInventory))
                {
                    part = parts[i];
                    return true;
                }
                if (reason == null) reason = candidateReason;
            }
            return false;
        }

        public static bool CanInstall(
            Pawn pawn,
            ThingDef itemDef,
            BodyPartRecord part,
            IReadOnlyList<MechModificationQueueEntry> queue,
            out string reason,
            bool checkInventory = true,
            bool allowEquivalentPart = false)
        {
            reason = null;
            ModificationProfile profile = ModificationProfileDatabase.Get(itemDef);
            if (pawn?.health?.hediffSet == null || profile?.properties == null)
            {
                reason = "FFF.MechModification.InvalidModification".Translate();
                return false;
            }
            if (!ModificationUtility.IsValidTargetPart(pawn, profile.properties, part, allowEquivalentPart))
            {
                reason = "FFF.Modification_NoValidPart".Translate();
                return false;
            }

            int pendingSameDef = CountQueuedItems(queue, itemDef);
            if (checkInventory)
            {
                MapComponent_ModificationIndex index = pawn.Map?.GetComponent<MapComponent_ModificationIndex>();
                int available = index?.CountAvailable(itemDef, pawn) ?? 0;
                if (pendingSameDef >= available)
                {
                    reason = "FFF.MechModification.NotEnoughItems".Translate(itemDef.LabelCap);
                    return false;
                }
            }

            Hediff existingSame = null;
            int installedCount = 0;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff?.TryGetComp<HediffComp_Modification>() == null || IsQueuedForRemoval(queue, hediff)) continue;
                if (profile.TargetsBodyPart)
                {
                    if (hediff.Part != part) continue;
                    if (hediff.def != profile.hediffDef)
                    {
                        reason = "FFF.MechModification.PartOccupied".Translate(part.LabelCap);
                        return false;
                    }
                    existingSame = hediff;
                    installedCount = GetInstalledCount(hediff);
                }
                else if (hediff.def == profile.hediffDef)
                {
                    existingSame = hediff;
                    installedCount = GetInstalledCount(hediff);
                }
            }

            int pendingSameTarget = 0;
            if (queue != null)
            {
                for (int i = 0; i < queue.Count; i++)
                {
                    MechModificationQueueEntry queued = queue[i];
                    if (queued == null || queued.kind != MechModificationOperationKind.Install || queued.itemDef == null) continue;
                    ModificationProfile queuedProfile = ModificationProfileDatabase.Get(queued.itemDef);
                    if (queuedProfile == null) continue;
                    if (profile.TargetsBodyPart)
                    {
                        if (queued.part != part) continue;
                        if (queuedProfile.hediffDef != profile.hediffDef)
                        {
                            reason = "FFF.MechModification.PartOccupied".Translate(part.LabelCap);
                            return false;
                        }
                        pendingSameTarget++;
                    }
                    else if (!queuedProfile.TargetsBodyPart && queuedProfile.hediffDef == profile.hediffDef)
                    {
                        pendingSameTarget++;
                    }
                }
            }

            if (existingSame != null || pendingSameTarget > 0)
            {
                if (!profile.mergeable)
                {
                    reason = "FFF.MechModification.Unique".Translate(itemDef.LabelCap);
                    return false;
                }
                int maximum = profile.GetMaxInstallations(pawn);
                if ((existingSame == null ? 0 : installedCount) + pendingSameTarget >= maximum)
                {
                    reason = "FFF.MechModification.StackFull".Translate(itemDef.LabelCap, maximum);
                    return false;
                }
            }
            return true;
        }

        private static int CountQueuedItems(IReadOnlyList<MechModificationQueueEntry> queue, ThingDef itemDef)
        {
            if (queue == null || itemDef == null) return 0;
            int count = 0;
            for (int i = 0; i < queue.Count; i++)
            {
                MechModificationQueueEntry entry = queue[i];
                if (entry?.kind == MechModificationOperationKind.Install && entry.itemDef == itemDef) count++;
            }
            return count;
        }

        private static bool IsQueuedForRemoval(IReadOnlyList<MechModificationQueueEntry> queue, Hediff hediff)
        {
            if (queue == null || hediff == null) return false;
            for (int i = 0; i < queue.Count; i++)
            {
                MechModificationQueueEntry entry = queue[i];
                if (entry?.kind == MechModificationOperationKind.Uninstall && entry.uninstallHediff == hediff) return true;
            }
            return false;
        }

        private static int GetInstalledCount(Hediff hediff)
        {
            HediffComp_Modification comp = hediff?.TryGetComp<HediffComp_Modification>();
            return comp == null ? 1 : comp.InstalledCount;
        }
    }
}
