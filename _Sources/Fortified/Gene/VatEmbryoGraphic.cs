using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 掛在 GeneDef 上：帶有此基因的胚胎在培育艙裡改用自訂貼圖，
    /// 而不是原版那顆人類胎兒（Other/VatGrownFetus_EarlyStage / _LateStage）。
    ///
    /// 兩個階段各自可留空，留空的一方會沿用另一方，所以只想給一張圖也行。
    /// 注意：原版會在繪製前強制覆寫 drawSize（依孕育進度在 0.4~0.95 之間插值），
    /// 所以這裡的 graphicData.drawSize 不會生效，尺寸請直接畫進 PNG 的留白裡。
    /// </summary>
    public class ModExtension_VatEmbryoGraphic : DefModExtension
    {
        /// <summary>前期（剩餘時間 > 9 天）使用的貼圖。</summary>
        public GraphicData earlyStage;

        /// <summary>後期使用的貼圖。</summary>
        public GraphicData lateStage;

        public GraphicData DataFor(bool early)
        {
            if (early)
            {
                return earlyStage ?? lateStage;
            }
            return lateStage ?? earlyStage;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (earlyStage == null && lateStage == null)
            {
                yield return "ModExtension_VatEmbryoGraphic 的 earlyStage 與 lateStage 都是空的。";
            }
            if (earlyStage != null && earlyStage.graphicClass == null)
            {
                yield return "ModExtension_VatEmbryoGraphic.earlyStage 缺少 graphicClass。";
            }
            if (lateStage != null && lateStage.graphicClass == null)
            {
                yield return "ModExtension_VatEmbryoGraphic.lateStage 缺少 graphicClass。";
            }
        }
    }

    public static class VatEmbryoGraphicUtility
    {
        public static ModExtension_VatEmbryoGraphic ExtensionFor(HumanEmbryo embryo)
        {
            GeneSet geneSet = embryo?.GeneSet;
            if (geneSet == null)
            {
                return null;
            }
            List<GeneDef> genes = geneSet.GenesListForReading;
            for (int i = 0; i < genes.Count; i++)
            {
                ModExtension_VatEmbryoGraphic ext = genes[i]?.GetModExtension<ModExtension_VatEmbryoGraphic>();
                if (ext != null)
                {
                    return ext;
                }
            }
            return null;
        }

        public static Graphic GraphicFor(Building_GrowthVat vat, bool early)
        {
            ModExtension_VatEmbryoGraphic ext = ExtensionFor(vat?.selectedEmbryo);
            // GraphicData.Graphic 自己有快取，不必再包一層。
            return ext?.DataFor(early)?.Graphic;
        }
    }

    /// <summary>
    /// 換掉培育艙拿來畫胎兒的那兩個私有屬性。
    ///
    /// 選擇 patch getter 而不是 DrawAt：原版在 DrawAt 裡負責尺寸插值、位置偏移、
    /// 頂蓋繪製與旋轉，全部照抄一遍既冗長又容易在改版時失準。只把「要畫哪張圖」
    /// 換掉，其餘行為完全交還原版。
    /// </summary>
    [HarmonyPatch(typeof(Building_GrowthVat), "FetusEarlyStage", MethodType.Getter)]
    public static class Patch_Building_GrowthVat_FetusEarlyStage
    {
        [HarmonyPostfix]
        public static void Postfix(Building_GrowthVat __instance, ref Graphic __result)
        {
            Graphic custom = VatEmbryoGraphicUtility.GraphicFor(__instance, early: true);
            if (custom != null)
            {
                __result = custom;
            }
        }
    }

    [HarmonyPatch(typeof(Building_GrowthVat), "FetusLateStage", MethodType.Getter)]
    public static class Patch_Building_GrowthVat_FetusLateStage
    {
        [HarmonyPostfix]
        public static void Postfix(Building_GrowthVat __instance, ref Graphic __result)
        {
            Graphic custom = VatEmbryoGraphicUtility.GraphicFor(__instance, early: false);
            if (custom != null)
            {
                __result = custom;
            }
        }
    }
}
