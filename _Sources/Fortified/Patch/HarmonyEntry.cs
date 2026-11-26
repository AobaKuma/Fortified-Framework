using Verse;
using HarmonyLib;

namespace Fortified
{
    [StaticConstructorOnStartup]
    public static class HarmonyEntry
    {
        static HarmonyEntry()
        {
            Harmony entry = new Harmony("Fortified");
            // 沒有分類的補丁照舊全掛；標了 [HarmonyPatchCategory] 的屬於可開關功能，由 FFF_FeatureRegistry 決定要不要掛
            entry.PatchAllUncategorized();
            FFF_FeatureRegistry.Resolve(entry);
            // 條件式 patch（找不到目標方法時靜默略過，不中斷啟動）
            Patch_DisableShuttleLaunch.TryApply(entry);
            // 初始化动态补丁管理器，但不挂载（只有任务激活时才挂载）
            FFF_CovertOpsPatchManager.Init();
        }
    }
}