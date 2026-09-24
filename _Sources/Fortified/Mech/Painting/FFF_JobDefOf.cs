using RimWorld;
using Verse;

namespace Fortified
{
    // 涂装Job定义引用
    [DefOf]
    public static class FFF_JobDefOf
    {
        public static JobDef FFF_PaintMech;
        public static JobDef FFF_WaitForPainting;

        // 設施封鎖：地表緊急解鎖 / Facility lockdown: surface emergency override
        public static JobDef FFF_LockdownOverride;
    }
}
