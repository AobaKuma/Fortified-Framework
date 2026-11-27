using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 轉移船、結構樁、平台拆除用到的 Def（2026-11 從 DMSE 移入，原 DMSE_FrameworkDefOf 的轉移部分）。
    /// 這些 Def 在 1.7/Odyssey 載入資料夾，只有 Odyssey 啟用時存在，所以不併進 FFF_DefOf。
    /// 舊名稱（DMSE_*）由 FFF_AerospaceRenames 的 DefRenameDef 對照。
    /// </summary>
    [DefOf]
    public static class FFF_AerospaceDefOf
    {
        // ── 轉移飛行 ──
        [MayRequireOdyssey] public static TransferFlightTuningDef FFF_TransferFlightTuning;
        [MayRequireOdyssey] public static JobDef FFF_BuildPileBeam;
        [MayRequireOdyssey] public static JobDef FFF_InitiateTransfer;
        [MayRequireOdyssey] public static WorldObjectDef FFF_TransferShip;
        [MayRequireOdyssey] public static MapGeneratorDef FFF_ShipVoid;
        [MayRequireOdyssey] public static GameConditionDef FFF_TransferGrace;

        // ── 太空平台拆除 ──
        [MayRequireOdyssey] public static DesignationDef FFF_RemovePlatform;
        [MayRequireOdyssey] public static JobDef FFF_RemovePlatformJob;
        [MayRequireOdyssey] public static TerrainAffordanceDef FFF_Platform;

        static FFF_AerospaceDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(FFF_AerospaceDefOf));
        }
    }
}
