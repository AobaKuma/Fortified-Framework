using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
namespace Fortified
{
    [HarmonyPatch(typeof(MainTabWindow_Architect), "CacheDesPanels")]
    public static class Patch_MainTabWindow_Architect_CacheDesPanels
    {
        private const string TargetCategoryDefName = "FFF_DevCategory";
        // AccessTools：高效 FieldRef（避免每次 FieldInfo.GetValue）
        private static readonly AccessTools.FieldRef<MainTabWindow_Architect, List<ArchitectCategoryTab>> DesPanelsCachedRef =
            AccessTools.FieldRefAccess<MainTabWindow_Architect, List<ArchitectCategoryTab>>("desPanelsCached");

        // 你的目標分類 defName
        private static readonly HashSet<string> HiddenInNonDevMode = new(StringComparer.Ordinal)
        {
            TargetCategoryDefName
        };

        // 開發模式且模組設定允許時才顯示目標分類
        public static bool ShowDevCategory => Prefs.DevMode && FortifiedMod.Settings.showDevCategory;

        // Postfix 保留原來由其他模組加入的面板，只在不顯示時移除目標分類
        public static void Postfix(MainTabWindow_Architect __instance)
        {
            if (ShowDevCategory)
                return; // 顯示時不處理，保留所有面板

            var list = DesPanelsCachedRef(__instance);
            if (list == null || list.Count == 0)
                return;

            // 保留其他模組可能加入的面板，只移除我們要隱藏的分類
            list.RemoveAll(tab => tab.def != null && HiddenInNonDevMode.Contains(tab.def.defName));

            // 若目前選中分類不在新列表中，清掉
            if (__instance.selectedDesPanel != null)
            {
                var selectedDefName = __instance.selectedDesPanel.def?.defName;
                if (selectedDefName != null && HiddenInNonDevMode.Contains(selectedDefName))
                {
                    __instance.selectedDesPanel = null;
                }
            }
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_Architect), nameof(MainTabWindow_Architect.PreOpen))]
    public static class Patch_MainTabWindow_Architect_PreOpen
    {
        // AccessTools：快取 private 方法委派（避免 MethodInfo.Invoke）
        private static readonly Action<MainTabWindow_Architect> CacheDesPanelsCall =
            AccessTools.MethodDelegate<Action<MainTabWindow_Architect>>(
                AccessTools.Method(typeof(MainTabWindow_Architect), "CacheDesPanels"));

        private static readonly Action<MainTabWindow_Architect> CacheSearchStateCall =
            AccessTools.MethodDelegate<Action<MainTabWindow_Architect>>(
                AccessTools.Method(typeof(MainTabWindow_Architect), "CacheSearchState"));

        private static bool? lastShowDevCategory;

        public static void Postfix(MainTabWindow_Architect __instance)
        {
            // 必須與 CacheDesPanels 的隱藏條件一致（DevMode 與模組設定），否則切換時不會重建面板
            bool current = Patch_MainTabWindow_Architect_CacheDesPanels.ShowDevCategory;
            if (lastShowDevCategory.HasValue && lastShowDevCategory.Value == current)
                return;

            lastShowDevCategory = current;
            CacheDesPanelsCall(__instance);
            CacheSearchStateCall(__instance);
        }
    }

    // 只隱藏建築選單分頁不夠：「複製」按鈕（BuildCopyCommandUtility）直接找 Designator_Build 並只看 Visible，
    // 開發分類的建築（石碑、遺跡等）因此能被玩家複製建造。分類不顯示時一併讓其建造指示器不可見；上帝模式照舊。
    [HarmonyPatch(typeof(Designator_Build), nameof(Designator_Build.Visible), MethodType.Getter)]
    public static class Patch_Designator_Build_Visible
    {
        public static void Postfix(Designator_Build __instance, ref bool __result)
        {
            if (!__result || DebugSettings.godMode || Patch_MainTabWindow_Architect_CacheDesPanels.ShowDevCategory)
                return;

            if (__instance.PlacingDef?.designationCategory?.defName == "FFF_DevCategory")
                __result = false;
        }
    }
}