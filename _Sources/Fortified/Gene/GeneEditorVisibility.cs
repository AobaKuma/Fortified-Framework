using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 任何掛在 GeneDef 上、且實作這個介面的 DefModExtension，都能把該基因
    /// 從基因編輯器（開局異種人編輯器）的可選清單裡藏起來。
    ///
    /// 用介面而不是寫死型別，是為了讓下游模組自己的擴展（例如 DMS 的
    /// MechBirthExtension）不必再多掛一個標記就能生效——框架不需要知道對方存在。
    /// </summary>
    public interface IHiddenGeneSource
    {
        bool HideInGeneEditor { get; }
    }

    /// <summary>
    /// 通用標記擴展：只為了隱藏而存在，不帶任何其他行為。
    /// 給那些沒有自己的擴展、又不想出現在編輯器裡的基因用。
    /// </summary>
    public class ModExtension_HiddenGene : DefModExtension, IHiddenGeneSource
    {
        public bool hideInGeneEditor = true;

        public bool HideInGeneEditor => hideInGeneEditor;
    }

    public static class GeneEditorVisibility
    {
        /// <summary>只在基因編輯器自己的繪製流程中為 true。</summary>
        public static bool DrawingGeneEditor;

        private static HashSet<GeneDef> hiddenGenes;
        private static List<GeneDef> cachedVisibleGenes;
        private static int cachedSourceCount = -1;

        public static bool IsHidden(GeneDef gene)
        {
            if (gene == null)
            {
                return false;
            }
            EnsureCache();
            return hiddenGenes.Contains(gene);
        }

        private static void EnsureCache()
        {
            if (hiddenGenes != null)
            {
                return;
            }
            hiddenGenes = new HashSet<GeneDef>();
            List<GeneDef> allGenes = DefDatabase<GeneDef>.AllDefsListForReading;
            for (int i = 0; i < allGenes.Count; i++)
            {
                GeneDef gene = allGenes[i];
                if (gene.modExtensions == null)
                {
                    continue;
                }
                for (int j = 0; j < gene.modExtensions.Count; j++)
                {
                    if (gene.modExtensions[j] is IHiddenGeneSource source && source.HideInGeneEditor)
                    {
                        hiddenGenes.Add(gene);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 回傳過濾後的清單。結果被快取，因為 GeneUtility.GenesInOrder 本身也是
        /// 啟動後就固定的靜態快取，編輯器每幀都會要一次。
        /// </summary>
        public static List<GeneDef> VisibleGenes(List<GeneDef> source)
        {
            EnsureCache();
            if (hiddenGenes.Count == 0)
            {
                return source;
            }
            if (cachedVisibleGenes == null || cachedSourceCount != source.Count)
            {
                cachedSourceCount = source.Count;
                cachedVisibleGenes = new List<GeneDef>(source.Count);
                for (int i = 0; i < source.Count; i++)
                {
                    if (!hiddenGenes.Contains(source[i]))
                    {
                        cachedVisibleGenes.Add(source[i]);
                    }
                }
            }
            return cachedVisibleGenes;
        }
    }

    /// <summary>
    /// 把過濾的作用範圍限制在編輯器自己的繪製流程內。
    ///
    /// 直接無條件過濾 GeneUtility.GenesInOrder 太危險——那份清單同時被基因生成、
    /// 異種人組裝等玩法邏輯讀取，少了幾個基因會影響存檔內容而不只是 UI。
    /// 這裡改成開關式：只有編輯器在畫自己的視窗內容時才生效。
    /// </summary>
    [HarmonyPatch(typeof(GeneCreationDialogBase), nameof(GeneCreationDialogBase.DoWindowContents))]
    public static class Patch_GeneCreationDialogBase_DoWindowContents
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            GeneEditorVisibility.DrawingGeneEditor = true;
        }

        // 用 Finalizer 而不是 Postfix：即使繪製過程中丟例外，旗標也一定會被關掉。
        [HarmonyFinalizer]
        public static void Finalizer()
        {
            GeneEditorVisibility.DrawingGeneEditor = false;
        }
    }

    [HarmonyPatch(typeof(GeneUtility), nameof(GeneUtility.GenesInOrder), MethodType.Getter)]
    public static class Patch_GeneUtility_GenesInOrder
    {
        [HarmonyPostfix]
        public static void Postfix(ref List<GeneDef> __result)
        {
            if (!GeneEditorVisibility.DrawingGeneEditor || __result == null)
            {
                return;
            }
            __result = GeneEditorVisibility.VisibleGenes(__result);
        }
    }
}
