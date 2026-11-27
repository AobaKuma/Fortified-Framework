using HarmonyLib;
using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 完成「雷達探測網」研究（<see cref="AirDefenseTuningDef.networkResearch"/>）時，教學世界地圖的 NPC 防空圈。
    /// <see cref="ConceptTeachUtility"/> 會自行確認條目存在、尚未學過，且當前是正常遊戲。
    /// </summary>
    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
    [HarmonyPatchCategory(FFF_AerospaceFeatures.MissilePatches)]
    public static class Patch_ResearchCompleted
    {
        public static void Postfix(ResearchProjectDef proj)
        {
            if (proj != null && proj == AirDefenseTuningDef.Current.networkResearch)
            {
                ConceptTeachUtility.TeachNPCAirDefenseCircles();
            }
        }
    }
}
