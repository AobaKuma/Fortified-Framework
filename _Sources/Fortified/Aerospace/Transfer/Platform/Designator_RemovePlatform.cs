using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>「拆除平台」：把太空平台地形拆回虛空並回收材料（加入 Odyssey 建築分頁）。</summary>
    public class Designator_RemovePlatform : Designator_Cells
    {
        public override bool DragDrawMeasurements => true;

        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.Floors;

        public Designator_RemovePlatform()
        {
            defaultLabel = "FFF.Platform.Remove".Translate();
            defaultDesc = "FFF.Platform.RemoveDesc".Translate();
            icon = ContentFinder<Texture2D>.Get("UI/Designators/RemoveBridge");
            useMouseIcon = true;
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded = SoundDefOf.Designate_RemoveBridge;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map) || c.Fogged(Map))
            {
                return false;
            }
            if (Map.designationManager.DesignationAt(c, FFF_AerospaceDefOf.FFF_RemovePlatform) != null)
            {
                return false;
            }
            if (!PlatformRemovalUtility.CanRemoveAt(c, Map, out string reason))
            {
                return reason != null ? new AcceptanceReport(reason) : AcceptanceReport.WasRejected;
            }
            return AcceptanceReport.WasAccepted;
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            if (DebugSettings.godMode)
            {
                PlatformRemovalUtility.Remove(c, Map, doLeavings: false);
            }
            else
            {
                Map.designationManager.AddDesignation(new Designation(c, FFF_AerospaceDefOf.FFF_RemovePlatform));
            }
        }

        public override void SelectedUpdate()
        {
            GenUI.RenderMouseoverBracket();
        }
    }
}
