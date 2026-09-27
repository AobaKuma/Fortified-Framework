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

        // Postfix 保留原來由其他模組加入的面板，只在非開發模式時移除目標分類
        public static void Postfix(MainTabWindow_Architect __instance)
        {
            bool devMode = Prefs.DevMode;
            if (devMode)
                return; // 開發模式下不處理，保留所有面板

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

        private static bool? lastDevMode;

        public static void Postfix(MainTabWindow_Architect __instance)
        {
            // 必須與 CacheDesPanels 的隱藏條件一致（只看 DevMode），否則切換開發模式時不會重建面板
            bool current = Prefs.DevMode;
            if (lastDevMode.HasValue && lastDevMode.Value == current)
                return;

            lastDevMode = current;
            CacheDesPanelsCall(__instance);
            CacheSearchStateCall(__instance);
        }
    }
}