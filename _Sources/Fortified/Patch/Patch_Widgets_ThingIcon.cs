using HarmonyLib;
using UnityEngine;
using Verse;

namespace Fortified
{
    // 修正機械體圖標在浮動選單（例如機械培育器「新增工單」）裡被上兩次色、變成近乎全黑的剪影。
    // 原版 ThingDef.ResolveIcon 替 CutoutComplex 等有 UI 版著色器的種族建立 uiIconMaterial，_Color 已是機體顏色，
    // uiIconColor 也記下同一個顏色；CutoutComplexUI 在遮罩區算 _Color × 頂點色。
    // 資訊卡、研究解鎖、工單設定不傳顏色，ThingIcon 會用白色頂點色；
    // FloatMenuOption.DoGUI 卻把 uiIconColor 當顏色傳入，遮罩區變成 機體色²（DMS 的 (100,102,100) 約剩 15% 亮度）。
    // 呼叫端傳入的正好是 uiIconColor、且會用 uiIconMaterial 畫時，改傳白色（保留透明度），結果與資訊卡相同。
    // 1.6 與 1.7 的這段原版程式碼相同，兩版都適用。
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ThingIcon),
        new[] { typeof(Rect), typeof(ThingDef), typeof(ThingDef), typeof(ThingStyleDef), typeof(float), typeof(Color?), typeof(int?), typeof(float) })]
    public static class Patch_Widgets_ThingIcon_MaskTint
    {
        public static void Prefix(ThingDef thingDef, ThingStyleDef thingStyleDef, ref Color? color)
        {
            // 種族圖標的 uiIconColor 就是 uiIconMaterial 的 _Color；其他 def 的 uiIconColor 可能另有來源，不處理
            if (!color.HasValue || thingDef?.race == null || thingDef.uiIconMaterial == null)
            {
                return;
            }
            // 有風格圖標時 Widgets.GetIconFor 不會用 uiIconMaterial
            if (thingStyleDef?.UIIcon != null)
            {
                return;
            }
            Color c = color.Value;
            Color iconColor = thingDef.uiIconColor;
            if (c.r != iconColor.r || c.g != iconColor.g || c.b != iconColor.b)
            {
                return;
            }
            color = new Color(1f, 1f, 1f, c.a);
        }
    }
}
