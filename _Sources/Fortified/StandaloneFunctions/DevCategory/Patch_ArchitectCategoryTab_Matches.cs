using HarmonyLib;
using RimWorld;
using System.Reflection;
using Verse;
namespace Fortified
{
    // 原版建築搜索只比對 Command.Label，下拉選單（Designator_Dropdown）的 Label 只是目前選中的子項，
    // 其他子項因此搜不到。這裡補上：下拉選單任一可見子項符合時也算符合。
    [HarmonyPatch]
    public static class Patch_ArchitectCategoryTab_Matches
    {
        private static readonly AccessTools.FieldRef<ArchitectCategoryTab, QuickSearchFilter> QuickSearchFilterRef =
            AccessTools.FieldRefAccess<ArchitectCategoryTab, QuickSearchFilter>("quickSearchFilter");

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ArchitectCategoryTab), "Matches");

        private static bool Prepare() => TargetMethod() != null;

        public static void Postfix(ArchitectCategoryTab __instance, Command c, ref bool __result)
        {
            if (__result || c is not Designator_Dropdown dropdown)
                return;

            var filter = QuickSearchFilterRef(__instance);
            foreach (var element in dropdown.Elements)
            {
                if (element.Visible && filter.Matches(element.Label))
                {
                    __result = true;
                    return;
                }
            }
        }
    }
}
