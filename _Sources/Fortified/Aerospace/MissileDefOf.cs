using RimWorld;
using Verse;
using Verse.AI;

namespace Fortified
{
    /// <summary>
    /// 飛彈、防空、電子戰、砲兵、導彈陣地用到的 Def（2026-11 從 DMSE 移入，合併原本散在各檔的 DMSE_*DefOf）。
    /// 這些 Def 在 1.7/Missiles 載入資料夾（不需要 Odyssey），1.6 沒有，所以不併進 FFF_DefOf（它也編進 1.6）。舊名稱由 FFF_MissileRenames 對照。
    /// </summary>
    [DefOf]
    public static class FFF_MissileDefOf
    {
        public static FleckDef FFF_InterceptFlash;
        public static FleckDef FFF_InterceptFireball;
        public static FleckDef FFF_InterceptFireGlow;
        public static FleckDef FFF_InterceptSparkStar;
        public static FleckDef FFF_InterceptSmokeBall;
        public static FleckDef FFF_InterceptDebrisMinor;
        public static FleckDef FFF_DebrisTrailPuff;
        public static ThingDef FFF_InterceptDebrisChunk;
        public static ThingDef FFF_Filth_MetalScrap;
        public static FleckDef FFF_LaunchSmokeLight;
        public static FleckDef FFF_LaunchSmokeMid;
        public static FleckDef FFF_LaunchSmokeDark;
        public static FleckDef FFF_LaunchGroundCloud;
        public static JobDef FFF_OperateWarRoom;
        public static DutyDef FFF_ManMissileStations;
        public static DutyDef FFF_MissileCrewDefend;
        public static KeyBindingDef FFF_MissileAssemblyPrevious;
        public static KeyBindingDef FFF_MissileAssemblyNext;
        public static JobDef FFF_DeliverMissileResource;
        public static JobDef FFF_AssembleMissile;
        public static JobDef FFF_LoadMissileLauncher;
        public static JobDef FFF_HaulMissileToRack;
        public static JobDef FFF_UnloadMissileRack;
        public static JobDef FFF_ManMissileLauncher;
        public static StatCategoryDef FFF_MissileProfile;
        public static StatCategoryDef FFF_MissileMechanics;
        public static StatCategoryDef FFF_MissilePart;
        public static JobDef FFF_AssembleBallisticSilo;
        public static JobDef FFF_ComputeFiringSolution;
        public static RulePackDef FFF_NamerStrikeMission;
        public static JobDef FFF_FireManPortableSAM;

        static FFF_MissileDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(FFF_MissileDefOf));
        }
    }
}
