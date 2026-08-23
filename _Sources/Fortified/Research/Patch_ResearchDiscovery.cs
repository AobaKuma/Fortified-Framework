using HarmonyLib;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 把 FFF 的「探明」判定接回原生的 <see cref="ResearchProjectDef.IsHidden"/>。
    ///
    /// 之所以選這個接點，是因為原生研究介面本來就已經圍繞 IsHidden 做了一整套隱藏表現：
    /// 節點標籤改為 (未知研究)、使用 HiddenResearchColor、不繪製底列圖示與 tooltip、
    /// 點擊不會選取、CanStartNow 為 false（無法開始研究）、快速搜尋排除、
    /// 書籍的 ReadingOutcomeDoerGainResearch 也不會灌進度。
    /// 直接沿用可省下對 MainTabWindow_Research 的大量 transpiler，遊戲改版時也不易壞。
    ///
    /// 只做 postfix 且僅在原本為 false 時才可能改為 true——不會蓋掉 Anomaly 的實體圖鑑隱藏。
    /// </summary>
    [HarmonyPatch(typeof(ResearchProjectDef), nameof(ResearchProjectDef.IsHidden), MethodType.Getter)]
    public static class Patch_ResearchProjectDef_IsHidden
    {
        [HarmonyPostfix]
        public static void Postfix(ResearchProjectDef __instance, ref bool __result)
        {
            // 原生已判定為隱藏就沒有再算一次的必要（本 getter 是每 frame × 每專案的熱路徑）。
            if (__result)
            {
                return;
            }
            // IsUndiscovered 內部已做重入保護與 try/catch，任何失敗都回傳 false。
            if (ResearchDiscoveryUtility.IsUndiscovered(__instance))
            {
                __result = true;
            }
        }
    }
}
