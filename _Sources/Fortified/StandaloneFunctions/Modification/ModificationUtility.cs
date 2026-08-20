using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Fortified
{
    public static class ModificationUtility
    {
        public static bool SupportedByRace(Pawn pawn, CompProperties_AddHediffOnTarget comp)
        {
            return pawn != null && comp != null && (comp.supportRaceDefs.NullOrEmpty() || comp.supportRaceDefs.Contains(pawn.def));
        }

        public static BodyPartRecord GetBodyPartRecord(Pawn pawn, CompProperties_AddHediffOnTarget props)
        {
            return HasSpaceToAttach(pawn, props, out BodyPartRecord part) ? part : null;
        }

        public static bool HasSpaceToAttach(Pawn pawn, CompProperties_AddHediffOnTarget comp, out BodyPartRecord bodyPart)
        {
            bodyPart = null;
            if (pawn?.RaceProps?.body == null || comp == null) return false;

            if (comp.targetBodyPartDefs.NullOrEmpty())
            {
                bodyPart = pawn.RaceProps.body.corePart;
                return bodyPart != null;
            }

            List<BodyPartRecord> parts = pawn.RaceProps.body.AllParts;
            for (int i = 0; i < parts.Count; i++)
            {
                if (!CanAttachToPart(pawn, comp, parts[i])) continue;
                bodyPart = parts[i];
                return true;
            }
            return false;
        }

        public static bool CanAttachToPart(Pawn pawn, CompProperties_AddHediffOnTarget comp, BodyPartRecord part)
        {
            if (!IsValidTargetPart(pawn, comp, part)) return false;
            if (comp.targetBodyPartDefs.NullOrEmpty()) return true;

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff?.Part == part && hediff.TryGetComp<HediffComp_Modification>() != null) return false;
            }
            return true;
        }

        public static bool IsValidTargetPart(Pawn pawn, CompProperties_AddHediffOnTarget comp, BodyPartRecord part, bool allowEquivalentPart = false)
        {
            if (pawn?.health?.hediffSet == null || pawn.RaceProps?.body == null || comp == null || part == null) return false;
            if (!SupportedByRace(pawn, comp) || pawn.health.hediffSet.PartIsMissing(part)) return false;
            if (comp.targetBodyPartDefs.NullOrEmpty()) return part == pawn.RaceProps.body.corePart;
            return allowEquivalentPart || comp.targetBodyPartDefs.Contains(part.def);
        }

        public static bool HasAnyBodyPartOf(Pawn pawn, List<BodyPartDef> partDefs)
        {
            return pawn?.RaceProps?.body != null && !pawn.RaceProps.body.AllParts.Where(p => partDefs.Contains(p.def)).EnumerableNullOrEmpty();
        }

        public static bool HasBodyPartOf(Pawn pawn, BodyPartDef partDef)
        {
            return pawn?.RaceProps?.body != null && !pawn.RaceProps.body.AllParts.Where(p => p.def == partDef).EnumerableNullOrEmpty();
        }
    }
}
