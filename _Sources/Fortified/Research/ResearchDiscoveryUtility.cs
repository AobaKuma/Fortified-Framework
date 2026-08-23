using System;
using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 研究「探明」機制的唯一判定點。
    ///
    /// 規則：掛有 <see cref="ModExtension_ResearchDiscovery"/> 的分頁／專案，在其前置研究
    /// 全部完成之前視為「未探明」；未探明的專案透過 <see cref="Patch_ResearchProjectDef_IsHidden"/>
    /// 接回原生 <see cref="ResearchProjectDef.IsHidden"/>，因此自動沿用原生的隱藏表現
    /// （顯示為 (未知研究)、不可點選、不可開始研究、快速搜尋排除、書籍不給進度）。
    ///
    /// 防禦性設計：所有失敗路徑一律 fail-open（回傳「已探明」）。隱藏機制失效只是多顯示東西，
    /// 誤判成隱藏卻會讓整棵研究樹永久不可研究，後者嚴重得多。
    /// </summary>
    public static class ResearchDiscoveryUtility
    {
        // 專案 -> 是否套用探明機制。Def 實例在 DefDatabase 生命週期內固定，可安全快取。
        private static Dictionary<ResearchProjectDef, bool> managedCache;

        // 套用探明機制的專案清單（供通知元件掃描用）。
        private static List<ResearchProjectDef> managedProjects;

        // 重入保護：本方法會讀取 IsFinished / PrerequisitesCompleted，若第三方 mod 也 patch 了
        // 那些成員並回頭呼叫 IsHidden，會形成無限遞迴。重入時直接視為已探明。
        [ThreadStatic] private static bool evaluating;

        // 例外只記錄一次：IsHidden 每 frame × 每專案都會被呼叫，重複 Log 會瞬間洗版。
        private static bool errorLogged;

        /// <summary>除錯開關：開啟後所有專案立即視為已探明（對應原生 debug_UnhideAllResearch）。</summary>
        public static bool DebugRevealAll;

        /// <summary>
        /// 取得對此專案生效的探明設定：專案自身的擴充優先，其次才是所屬分頁的擴充。
        /// 兩者皆無時回傳 null（＝不套用探明機制）。
        /// </summary>
        public static ModExtension_ResearchDiscovery ExtensionFor(ResearchProjectDef proj)
        {
            if (proj == null)
            {
                return null;
            }
            ModExtension_ResearchDiscovery own = proj.GetModExtension<ModExtension_ResearchDiscovery>();
            if (own != null)
            {
                return own;
            }
            return proj.tab != null ? proj.tab.GetModExtension<ModExtension_ResearchDiscovery>() : null;
        }

        /// <summary>此專案是否受探明機制管理。</summary>
        public static bool IsManaged(ResearchProjectDef proj)
        {
            if (proj == null)
            {
                return false;
            }
            if (managedCache == null)
            {
                managedCache = new Dictionary<ResearchProjectDef, bool>();
            }
            if (managedCache.TryGetValue(proj, out bool cached))
            {
                return cached;
            }

            bool result;
            try
            {
                ModExtension_ResearchDiscovery ext = ExtensionFor(proj);
                result = ext != null && ext.hideUntilDiscovered;
            }
            catch (Exception ex)
            {
                LogOnce($"[FFF] ResearchDiscoveryUtility.IsManaged failed for {proj.defName}: {ex}");
                result = false;
            }

            managedCache[proj] = result;
            return result;
        }

        /// <summary>所有受探明機制管理的專案（首次呼叫時建立，之後直接重用）。</summary>
        public static List<ResearchProjectDef> ManagedProjects
        {
            get
            {
                if (managedProjects != null)
                {
                    return managedProjects;
                }
                managedProjects = new List<ResearchProjectDef>();
                List<ResearchProjectDef> all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
                if (all == null)
                {
                    return managedProjects;
                }
                for (int i = 0; i < all.Count; i++)
                {
                    if (IsManaged(all[i]))
                    {
                        managedProjects.Add(all[i]);
                    }
                }
                return managedProjects;
            }
        }

        /// <summary>
        /// 此專案目前是否「未探明」。未受管理、已完成、或任何判定失敗的情況一律回傳 false。
        /// </summary>
        public static bool IsUndiscovered(ResearchProjectDef proj)
        {
            if (proj == null || DebugRevealAll)
            {
                return false;
            }
            // 重入 → fail-open。
            if (evaluating)
            {
                return false;
            }
            if (!IsManaged(proj))
            {
                return false;
            }
            // 尚未進入遊戲（主選單、載入中、Def 檢查階段）時不做判定：
            // IsFinished 會經由 ResearchManager 取進度，此時存取會拋例外。
            if (Current.Game == null || Find.ResearchManager == null)
            {
                return false;
            }
            // 外力強制探明（解封物品、事件、任務獎勵）永久有效，且優先於前置研究的推導結果。
            // 只是查一個 HashSet，且內部已 try/catch，放在重入旗標之外是安全的。
            if (GameComponent_ResearchDiscovery.IsForceDiscoveredSafe(proj))
            {
                return false;
            }

            evaluating = true;
            try
            {
                if (proj.IsFinished)
                {
                    return false;
                }
                return !proj.PrerequisitesCompleted;
            }
            catch (Exception ex)
            {
                LogOnce($"[FFF] ResearchDiscoveryUtility.IsUndiscovered failed for {proj.defName}; " +
                        $"discovery gating disabled for safety: {ex}");
                return false;
            }
            finally
            {
                evaluating = false;
            }
        }

        /// <summary>此專案是否已探明（受管理但尚未探明者以外皆為 true）。</summary>
        public static bool IsDiscovered(ResearchProjectDef proj)
        {
            return proj != null && !IsUndiscovered(proj);
        }

        /// <summary>
        /// 強制探明一個專案（解封物品、事件、任務獎勵用）。狀態存檔且永久有效，
        /// 會蓋過「前置研究尚未完成」的推導結果。
        ///
        /// 回傳 true 表示這次呼叫真的解封了某個原本未探明的專案；
        /// 專案為 null、尚未進入遊戲、或它本來就已探明時回傳 false。
        /// </summary>
        /// <param name="suppressLetter">
        /// true（預設）＝同時標記為「已通知」，由呼叫方自行告知玩家，不會再多出一封通用探明信。
        /// </param>
        public static bool ForceDiscover(ResearchProjectDef proj, bool suppressLetter = true)
        {
            if (proj == null)
            {
                return false;
            }
            try
            {
                GameComponent_ResearchDiscovery comp = GameComponent_ResearchDiscovery.CompSafe;
                if (comp == null)
                {
                    return false;
                }
                return comp.ForceDiscover(proj, suppressLetter);
            }
            catch (Exception ex)
            {
                Log.Error($"[FFF] ResearchDiscoveryUtility.ForceDiscover failed for {proj.defName}: {ex}");
                return false;
            }
        }

        /// <summary>清空快取。僅供除錯／熱重載使用。</summary>
        public static void ClearCache()
        {
            managedCache = null;
            managedProjects = null;
            errorLogged = false;
        }

        private static void LogOnce(string message)
        {
            if (errorLogged)
            {
                return;
            }
            errorLogged = true;
            Log.Error(message);
        }

        [DebugAction("Fortified", "Toggle research discovery / 切換研究探明限制",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ToggleDiscoveryGating()
        {
            DebugRevealAll = !DebugRevealAll;
            Messages.Message(
                DebugRevealAll
                    ? "[FFF] Research discovery gating OFF (all projects revealed)."
                    : "[FFF] Research discovery gating ON.",
                MessageTypeDefOf.TaskCompletion,
                historical: false);
        }
    }
}
