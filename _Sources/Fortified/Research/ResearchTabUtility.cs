using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// Helper utility for custom multi-category (knowledge) research tabs.
    /// A tab opts in by carrying a <see cref="ModExtension_UniqueResearchTab"/> with at least one category.
    /// </summary>
    public static class ResearchTabUtility
    {
        // Cache: knowledge category -> owning tab (resolved via ModExtension_UniqueResearchTab).
        private static Dictionary<KnowledgeCategoryDef, ResearchTabDef> categoryTabCache;

        /// <summary>
        /// Whether the given research tab should use the custom multi-category UI.
        /// True only when the tab carries a ModExtension_UniqueResearchTab with categories.
        /// </summary>
        public static bool ShouldUseUniqueTabUI(ResearchTabDef tabDef)
        {
            if (tabDef == null)
            {
                return false;
            }
            var ext = tabDef.GetModExtension<ModExtension_UniqueResearchTab>();
            return ext != null && ext.categories != null && ext.categories.Count > 0;
        }

        /// <summary>
        /// Number of active project slots (columns) for a research tab. 1 for normal tabs.
        /// </summary>
        public static int GetProjectSlotCount(ResearchTabDef tabDef)
        {
            if (tabDef != null)
            {
                var ext = tabDef.GetModExtension<ModExtension_UniqueResearchTab>();
                if (ext != null && ext.categories != null && ext.categories.Count > 0)
                {
                    return ext.categories.Count;
                }
            }
            return 1;
        }

        /// <summary>
        /// Returns the knowledge categories to display for a multi-category research tab
        /// (empty list for normal tabs).
        /// </summary>
        public static List<KnowledgeCategoryDef> GetCategoriesForTab(ResearchTabDef tabDef)
        {
            var list = new List<KnowledgeCategoryDef>();
            if (tabDef == null)
            {
                return list;
            }
            var ext = tabDef.GetModExtension<ModExtension_UniqueResearchTab>();
            if (ext != null && ext.categories != null && ext.categories.Count > 0)
            {
                list.AddRange(ext.categories);
            }
            return list;
        }

        /// <summary>
        /// Finds the research tab whose ModExtension_UniqueResearchTab contains the given category.
        /// Returns null when the category is not owned by any custom tab.
        /// </summary>
        public static ResearchTabDef FindTabForCategory(KnowledgeCategoryDef category)
        {
            if (category == null)
            {
                return null;
            }
            if (categoryTabCache == null)
            {
                categoryTabCache = new Dictionary<KnowledgeCategoryDef, ResearchTabDef>();
                foreach (ResearchTabDef tab in DefDatabase<ResearchTabDef>.AllDefsListForReading)
                {
                    var ext = tab.GetModExtension<ModExtension_UniqueResearchTab>();
                    if (ext == null || ext.categories == null)
                    {
                        continue;
                    }
                    foreach (KnowledgeCategoryDef cat in ext.categories)
                    {
                        if (cat != null && !categoryTabCache.ContainsKey(cat))
                        {
                            categoryTabCache[cat] = tab;
                        }
                    }
                }
            }
            return categoryTabCache.TryGetValue(category, out ResearchTabDef result) ? result : null;
        }

        /// <summary>
        /// Safely returns the currently-active research project for a knowledge category.
        ///
        /// The per-category "current project" state lives in ResearchManager.CurrentAnomalyKnowledgeProjects,
        /// which the base game only initializes while the Anomaly DLC is active. Without Anomaly that backing
        /// list is null, and ResearchManager.GetProject(category) throws a NullReferenceException as it tries
        /// to enumerate it. This wrapper guards every one of those preconditions so callers degrade gracefully
        /// (returning null = "no active project for this category") instead of crashing.
        /// </summary>
        public static ResearchProjectDef GetActiveProjectForCategory(KnowledgeCategoryDef category)
        {
            if (category == null)
            {
                return null;
            }

            // Per-category active-project tracking is an Anomaly-only mechanic.
            if (!ModsConfig.AnomalyActive)
            {
                return null;
            }

            var manager = Find.ResearchManager;
            if (manager == null || manager.CurrentAnomalyKnowledgeProjects == null)
            {
                return null;
            }

            return manager.GetProject(category);
        }
    }
}
