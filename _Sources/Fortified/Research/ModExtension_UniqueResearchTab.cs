using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// Attach to a ResearchTabDef to turn it into a multi-category (Anomaly-style) knowledge tab.
    /// Categories are displayed as columns, ordered left-to-right; knowledge injected into an
    /// earlier category overflows into later ones (see <see cref="KnowledgeUtility"/>).
    /// </summary>
    public class ModExtension_UniqueResearchTab : DefModExtension
    {
        // The list of KnowledgeCategoryDefs to display as vertical columns (left-to-right)
        public List<KnowledgeCategoryDef> categories = new List<KnowledgeCategoryDef>();
    }
}
