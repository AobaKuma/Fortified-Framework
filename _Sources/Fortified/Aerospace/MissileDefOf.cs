using RimWorld;
using Verse;
using Verse.AI;

namespace Fortified
{
    /// <summary>
    /// 飛彈、防空、電子戰、砲兵、導彈陣地用到的 Def（2026-11 從 DMSE 移入，合併原本散在各檔的 DMSE_*DefOf）。
    /// 這些 Def 在 1.7/Odyssey 載入資料夾，所以不併進 FFF_DefOf（它也編進 1.6）。舊名稱由 FFF_MissileRenames 對照。
    /// </summary>
    [DefOf]
    public static class FFF_MissileDefOf
    {
        [MayRequireOdyssey] public static FleckDef FFF_InterceptFlash;
        [MayRequireOdyssey] public static FleckDef FFF_InterceptFireball;
        [MayRequireOdyssey] public static FleckDef FFF_InterceptFireGlow;
        [MayRequireOdyssey] public static FleckDef FFF_InterceptSparkStar;
        [MayRequireOdyssey] public static FleckDef FFF_InterceptSmokeBall;
        [MayRequireOdyssey] public static FleckDef FFF_InterceptDebrisMinor;
        [MayRequireOdyssey] public static FleckDef FFF_DebrisTrailPuff;
        [MayRequireOdyssey] public static ThingDef FFF_InterceptDebrisChunk;
        [MayRequireOdyssey] public static ThingDef FFF_Filth_MetalScrap;
        [MayRequireOdyssey] public static FleckDef FFF_LaunchSmokeLight;
        [MayRequireOdyssey] public static FleckDef FFF_LaunchSmokeMid;
        [MayRequireOdyssey] public static FleckDef FFF_LaunchSmokeDark;
        [MayRequireOdyssey] public static FleckDef FFF_LaunchGroundCloud;
        [MayRequireOdyssey] public static JobDef FFF_OperateWarRoom;
        [MayRequireOdyssey] public static DutyDef FFF_ManMissileStations;
        [MayRequireOdyssey] public static DutyDef FFF_MissileCrewDefend;
        [MayRequireOdyssey] public static KeyBindingDef FFF_MissileAssemblyPrevious;
        [MayRequireOdyssey] public static KeyBindingDef FFF_MissileAssemblyNext;
        [MayRequireOdyssey] public static JobDef FFF_DeliverMissileResource;
        [MayRequireOdyssey] public static JobDef FFF_AssembleMissile;
        [MayRequireOdyssey] public static JobDef FFF_LoadMissileLauncher;
        [MayRequireOdyssey] public static JobDef FFF_HaulMissileToRack;
        [MayRequireOdyssey] public static JobDef FFF_UnloadMissileRack;
        [MayRequireOdyssey] public static JobDef FFF_ManMissileLauncher;
        [MayRequireOdyssey] public static StatCategoryDef FFF_MissileProfile;
        [MayRequireOdyssey] public static StatCategoryDef FFF_MissileMechanics;
        [MayRequireOdyssey] public static StatCategoryDef FFF_MissilePart;
        [MayRequireOdyssey] public static JobDef FFF_AssembleBallisticSilo;
        [MayRequireOdyssey] public static JobDef FFF_ComputeFiringSolution;
        [MayRequireOdyssey] public static RulePackDef FFF_NamerStrikeMission;
        [MayRequireOdyssey] public static JobDef FFF_FireManPortableSAM;

        static FFF_MissileDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(FFF_MissileDefOf));
        }
    }
}
