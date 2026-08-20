using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace Fortified
{
    public interface IModificationMergeParticipant
    {
        bool CanMergeModificationFrom(HediffComp other);
    }

    public interface IModificationMergeCapacityProvider
    {
        int GetMaxModificationInstallations(Pawn pawn);
    }

    public sealed class ModificationProfile
    {
        public ThingDef itemDef;
        public HediffDef hediffDef;
        public CompProperties_AddHediffOnTarget properties;
        public bool mergeable;

        public bool TargetsBodyPart => properties?.targetBodyPartDefs.NullOrEmpty() == false;

        public int GetMaxInstallations(Pawn pawn)
        {
            if (!mergeable) return 1;
            int maximum = int.MaxValue;
            List<HediffCompProperties> comps = hediffDef?.comps;
            if (comps == null) return maximum;
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i] is IModificationMergeCapacityProvider provider)
                {
                    maximum = Math.Min(maximum, Math.Max(1, provider.GetMaxModificationInstallations(pawn)));
                }
            }
            return maximum;
        }
    }

    [StaticConstructorOnStartup]
    public static class ModificationProfileDatabase
    {
        private static readonly Dictionary<ThingDef, ModificationProfile> Profiles = new Dictionary<ThingDef, ModificationProfile>();
        private static readonly Dictionary<HediffDef, ThingDef> SourcesByHediff = new Dictionary<HediffDef, ThingDef>();
        private static readonly Dictionary<Type, bool> MergeOverrideCache = new Dictionary<Type, bool>();

        static ModificationProfileDatabase()
        {
            Rebuild();
        }

        public static IEnumerable<ThingDef> ModificationDefs => Profiles.Keys;

        public static bool IsModificationDef(ThingDef def)
        {
            return def != null && Profiles.ContainsKey(def);
        }

        public static ModificationProfile Get(ThingDef def)
        {
            return def != null && Profiles.TryGetValue(def, out ModificationProfile profile) ? profile : null;
        }

        public static ThingDef GetSource(HediffDef def)
        {
            return def != null && SourcesByHediff.TryGetValue(def, out ThingDef source) ? source : null;
        }

        public static bool CanMerge(Hediff existing, Hediff incoming)
        {
            if (existing == null || incoming == null || existing.def != incoming.def) return false;
            List<HediffComp> existingComps = (existing as HediffWithComps)?.comps;
            List<HediffComp> incomingComps = (incoming as HediffWithComps)?.comps;
            if (existingComps == null || incomingComps == null) return false;

            bool foundParticipant = false;
            for (int i = 0; i < existingComps.Count; i++)
            {
                HediffComp current = existingComps[i];
                if (current is HediffComp_Modification) continue;
                HediffComp other = FindMatchingComp(incomingComps, current.GetType());
                if (other == null) return false;
                if (current is IModificationMergeParticipant participant)
                {
                    foundParticipant = true;
                    if (!participant.CanMergeModificationFrom(other)) return false;
                }
                else if (OverridesCompPostMerged(current.GetType()))
                {
                    foundParticipant = true;
                }
                else
                {
                    return false;
                }
            }
            return foundParticipant;
        }

        private static void Rebuild()
        {
            Profiles.Clear();
            SourcesByHediff.Clear();
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                CompProperties_AddHediffOnTarget properties = def.GetCompProperties<CompProperties_AddHediffOnTarget>();
                if (properties?.hediffDef == null) continue;
                ModificationProfile profile = new ModificationProfile
                {
                    itemDef = def,
                    hediffDef = properties.hediffDef,
                    properties = properties,
                    mergeable = IsSafelyMergeable(properties.hediffDef)
                };
                Profiles[def] = profile;
                if (!SourcesByHediff.ContainsKey(properties.hediffDef)) SourcesByHediff.Add(properties.hediffDef, def);
            }
        }

        private static bool IsSafelyMergeable(HediffDef def)
        {
            List<HediffCompProperties> comps = def?.comps;
            if (comps == null) return false;
            bool foundParticipant = false;
            for (int i = 0; i < comps.Count; i++)
            {
                Type compClass = comps[i]?.compClass;
                if (compClass == null || compClass == typeof(HediffComp_Modification)) continue;
                if (typeof(IModificationMergeParticipant).IsAssignableFrom(compClass) || OverridesCompPostMerged(compClass))
                {
                    foundParticipant = true;
                    continue;
                }
                return false;
            }
            return foundParticipant;
        }

        private static bool OverridesCompPostMerged(Type type)
        {
            if (type == null) return false;
            if (MergeOverrideCache.TryGetValue(type, out bool cached)) return cached;
            MethodInfo method = type.GetMethod(nameof(HediffComp.CompPostMerged), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            bool result = method != null && method.DeclaringType != typeof(HediffComp);
            MergeOverrideCache[type] = result;
            return result;
        }

        private static HediffComp FindMatchingComp(List<HediffComp> comps, Type type)
        {
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i]?.GetType() == type) return comps[i];
            }
            return null;
        }
    }
}
