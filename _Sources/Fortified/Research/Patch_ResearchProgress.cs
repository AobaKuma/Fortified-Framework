using HarmonyLib;
using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 讓知識型研究專案在「沒有 Anomaly DLC」時也能記錄進度。
    ///
    /// 原生 ResearchManager.GetProgress 對知識型專案（baseCost==0 且 knowledgeCost&gt;0）
    /// 只有在 ModsConfig.AnomalyActive 時才回傳實際知識點，否則永遠回傳 0，
    /// 導致 ProgressPercent / IsFinished 皆失效。
    ///
    /// 本 postfix 僅在無 Anomaly 時介入，且只針對知識型專案，把結果改為
    /// <see cref="GameComponent_KnowledgeStore"/> 保存的點數。其餘專案完全不受影響。
    /// </summary>
    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.GetProgress))]
    public static class Patch_ResearchManager_GetProgress
    {
        [HarmonyPostfix]
        public static void Postfix(ResearchProjectDef proj, ref float __result)
        {
            // Anomaly 啟用時交由原生機制，不介入。
            if (ModsConfig.AnomalyActive)
            {
                return;
            }
            if (proj == null)
            {
                return;
            }
            // 一般（baseCost）研究不碰。
            if (proj.baseCost > 0f)
            {
                return;
            }
            // 非知識型專案不碰。
            if (proj.knowledgeCost <= 0f)
            {
                return;
            }

            GameComponent_KnowledgeStore store = GameComponent_KnowledgeStore.CompSafe;
            if (store == null)
            {
                return;
            }

            __result = store.GetStored(proj);
        }
    }

    /// <summary>
    /// 讓 FinishProject 在「沒有 Anomaly DLC」時也能完成知識型專案。
    ///
    /// 原生 ResearchManager.FinishProject 對知識型專案（baseCost==0 且 knowledgeCost&gt;0）
    /// 只有在 ModsConfig.AnomalyActive 時才把 anomalyKnowledge 設滿，否則不記任何進度；
    /// 而上面的 GetProgress postfix 只讀 <see cref="GameComponent_KnowledgeStore"/>，結果「完成」後仍顯示未完成。
    /// 受影響的有：ScenPart_StartingResearch（開局給的研究）、研究分頁的開發者「完成研究」、
    /// 以及其他直接呼叫 FinishProject 的 mod。
    ///
    /// 用 prefix：原生在 FinishProject 中途（前置遞迴、解鎖通知）就會讀 IsFinished，要先補滿。
    /// KnowledgeUtility 自己完成專案時也是先補滿再呼叫 FinishProject，重複設定無害。
    /// </summary>
    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
    public static class Patch_ResearchManager_FinishProject
    {
        [HarmonyPrefix]
        public static void Prefix(ResearchProjectDef proj)
        {
            // Anomaly 啟用時原生會自己設滿 anomalyKnowledge，不介入。
            if (ModsConfig.AnomalyActive || proj == null)
            {
                return;
            }
            // 只處理知識型專案。
            if (proj.baseCost > 0f || proj.knowledgeCost <= 0f)
            {
                return;
            }
            GameComponent_KnowledgeStore.CompSafe?.SetStored(proj, proj.knowledgeCost);
        }
    }
}
