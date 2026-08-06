using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.Steam;

namespace Fortified
{
    /// <summary>
    /// 讓模組資料夾內的 .rimignore 在上傳創意工坊時生效。
    ///
    /// 流程：
    ///   1. Workshop.Upload 前置攔截 → 讀取 .rimignore、掃描出排除清單 → 顯示預覽視窗。
    ///   2. 使用者確認後建立暫存鏡像，再以旁路旗標重新呼叫原本的 Upload。
    ///   3. ModMetaData.GetWorkshopUploadDirectory 後置導向鏡像資料夾，
    ///      Steam 便只會看到過濾後的內容。
    ///   4. 上傳進度視窗關閉時清除鏡像。
    ///
    /// 任何一步失敗都不會靜默放行：要嘛完整過濾上傳，要嘛中止並告知使用者。
    /// </summary>
    [HarmonyPatch]
    public static class Patch_WorkshopIgnore
    {
        private static RimIgnoreSession activeSession;

        /// <summary>重新進入 Upload 時的旁路旗標，避免無限遞迴。</summary>
        private static bool bypassInterception;

        private static readonly MethodInfo UploadMethod =
            AccessTools.Method(typeof(Workshop), "Upload", new[] { typeof(WorkshopUploadable) });

        /// <summary>目前是否有作業中的過濾上傳（供除錯查詢）。</summary>
        public static bool HasActiveSession => activeSession != null;

        // ── 1. 攔截上傳 ─────────────────────────────────────────────

        [HarmonyPatch(typeof(Workshop), "Upload")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Workshop_Upload_Prefix(WorkshopUploadable item)
        {
            if (bypassInterception)
            {
                return true;
            }

            ModMetaData mod = item as ModMetaData;
            if (mod == null)
            {
                return true; // 劇本等其他上傳類型不處理
            }

            DirectoryInfo root;
            string ignorePath;
            try
            {
                root = mod.RootDir;
                if (root == null || !root.Exists)
                {
                    return true;
                }
                ignorePath = Path.Combine(root.FullName, RimIgnoreSpec.FileName);
                if (!File.Exists(ignorePath))
                {
                    return true; // 沒有 .rimignore → 完全維持原版行為
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Fortified] 檢查 {RimIgnoreSpec.FileName} 時發生錯誤，維持原版上傳流程：{e}");
                return true;
            }

            // 到這裡代表使用者明確放了 .rimignore，之後的錯誤一律中止而非放行。
            try
            {
                DiscardActiveSession();

                RimIgnoreSpec spec = RimIgnoreSpec.Load(ignorePath);
                if (spec == null)
                {
                    ShowBlockingError(item, RimIgnoreText.Get(RimIgnoreText.ErrLoadFailed,
                        "無法讀取 {0}，已中止上傳。詳情請見開發者主控台。", RimIgnoreSpec.FileName));
                    return false;
                }

                RimIgnorePlan plan = RimIgnorePlan.Build(root, spec);
                if (plan == null)
                {
                    ShowBlockingError(item, RimIgnoreText.Get(RimIgnoreText.ErrScanFailed,
                        "無法掃描模組資料夾，已中止上傳。詳情請見開發者主控台。"));
                    return false;
                }

                if (!plan.HasExclusions && plan.Warnings.Count == 0)
                {
                    return true; // 沒有任何項目被排除 → 直接走原版流程
                }

                Find.WindowStack.Add(new Dialog_RimIgnoreConfirm(
                    plan,
                    mod.Name,
                    ignorePath,
                    confirmedPlan => BeginFilteredUpload(item, mod, confirmedPlan),
                    () => UploadWithoutFiltering(item)));
                return false;
            }
            catch (Exception e)
            {
                Log.Error($"[Fortified] {RimIgnoreSpec.FileName} 前置處理失敗：{e}");
                ShowBlockingError(item, RimIgnoreText.Get(RimIgnoreText.ErrUnexpected,
                    "套用忽略規則時發生未預期的錯誤，已中止上傳。詳情請見開發者主控台。"));
                return false;
            }
        }

        // ── 2. 建立鏡像並重新進入上傳 ────────────────────────────────

        private static void BeginFilteredUpload(WorkshopUploadable item, ModMetaData mod, RimIgnorePlan plan)
        {
            RimIgnoreSession session;
            try
            {
                session = RimIgnoreStaging.Build(mod.RootDir, plan, mod.Name);
            }
            catch (Exception e)
            {
                Log.Error($"[Fortified] 建立上傳暫存鏡像失敗：{e}");
                ShowBlockingError(item, RimIgnoreText.Get(RimIgnoreText.ErrStagingFailed,
                    "建立暫存鏡像失敗，已中止上傳（模組原始檔案未被更動）。\n\n{0}", e.Message));
                return;
            }

            activeSession = session;
            Log.Message($"[Fortified] {RimIgnoreSpec.FileName}：已排除 {plan.ExcludedEntries.Count} 個項目／" +
                        $"{plan.ExcludedFileCount} 個檔案（{RimIgnorePlan.FormatBytes(plan.ExcludedBytes)}），" +
                        $"鏡像 {session.LinkedCount} 個硬連結、{session.CopiedCount} 個複製檔。");

            bool started = false;
            try
            {
                started = InvokeOriginalUpload(item);
            }
            finally
            {
                if (!started || Workshop.CurStage == WorkshopInteractStage.None)
                {
                    // 上傳沒有真的啟動（例如已有其他上傳進行中）→ 立刻收拾鏡像。
                    DiscardActiveSession();
                }
            }
        }

        private static void UploadWithoutFiltering(WorkshopUploadable item)
        {
            DiscardActiveSession();
            Log.Warning($"[Fortified] 使用者選擇忽略 {RimIgnoreSpec.FileName}，本次為完整上傳。");
            InvokeOriginalUpload(item);
        }

        private static bool InvokeOriginalUpload(WorkshopUploadable item)
        {
            if (UploadMethod == null)
            {
                Log.Error("[Fortified] 找不到 Verse.Steam.Workshop.Upload，無法續行上傳。");
                return false;
            }

            bypassInterception = true;
            try
            {
                UploadMethod.Invoke(null, new object[] { item });
                return true;
            }
            catch (TargetInvocationException e)
            {
                Log.Error("[Fortified] 呼叫原始上傳流程時發生錯誤：" + (e.InnerException ?? e));
                return false;
            }
            catch (Exception e)
            {
                Log.Error("[Fortified] 呼叫原始上傳流程時發生錯誤：" + e);
                return false;
            }
            finally
            {
                bypassInterception = false;
            }
        }

        // ── 3. 把上傳內容路徑導向鏡像 ───────────────────────────────

        [HarmonyPatch(typeof(ModMetaData), nameof(ModMetaData.GetWorkshopUploadDirectory))]
        [HarmonyPostfix]
        public static void ModMetaData_GetWorkshopUploadDirectory_Postfix(ModMetaData __instance, ref DirectoryInfo __result)
        {
            RimIgnoreSession session = activeSession;
            if (session?.StagingDirectory == null)
            {
                return;
            }

            try
            {
                DirectoryInfo root = __instance?.RootDir;
                if (root == null || !PathEquals(root.FullName, session.ModRootFullPath))
                {
                    return;
                }

                session.StagingDirectory.Refresh();
                if (!session.StagingDirectory.Exists)
                {
                    Log.Error("[Fortified] 暫存鏡像已不存在，本次將以原始資料夾上傳。");
                    return;
                }

                RimIgnoreStaging.SyncPublishedFileId(session);
                __result = session.StagingDirectory;
                session.Redirected = true;
            }
            catch (Exception e)
            {
                Log.Error($"[Fortified] 導向暫存鏡像失敗，本次將以原始資料夾上傳：{e}");
            }
        }

        // ── 4. 上傳結束後清理 ──────────────────────────────────────

        [HarmonyPatch(typeof(Window), nameof(Window.PostClose))]
        [HarmonyPostfix]
        public static void Window_PostClose_Postfix(Window __instance)
        {
            if (!(__instance is Dialog_WorkshopOperationInProgress))
            {
                return;
            }
            if (activeSession == null)
            {
                return;
            }
            try
            {
                DiscardActiveSession();
            }
            catch (Exception e)
            {
                Log.Warning($"[Fortified] 清除上傳暫存鏡像時發生錯誤：{e.Message}");
            }
        }

        /// <summary>結束目前 session 並清除鏡像。可安全重複呼叫。</summary>
        public static void DiscardActiveSession()
        {
            RimIgnoreSession session = activeSession;
            activeSession = null;
            if (session != null)
            {
                RimIgnoreStaging.Cleanup(session);
            }
        }

        private static void ShowBlockingError(WorkshopUploadable item, string message)
        {
            try
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    message,
                    RimIgnoreText.Get(RimIgnoreText.UploadAnyway, "仍要完整上傳"),
                    () => UploadWithoutFiltering(item),
                    RimIgnoreText.Get(RimIgnoreText.Cancel, "Cancel"),
                    null,
                    RimIgnoreText.Get(RimIgnoreText.ErrorTitle, "忽略規則處理失敗")));
            }
            catch (Exception e)
            {
                Log.Error("[Fortified] 無法顯示錯誤視窗：" + e);
            }
        }

        private static bool PathEquals(string a, string b)
        {
            if (a == null || b == null)
            {
                return false;
            }
            return string.Equals(
                a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>遊戲啟動時掃除上次上傳可能殘留的暫存鏡像。</summary>
    [StaticConstructorOnStartup]
    public static class RimIgnoreStartupSweep
    {
        static RimIgnoreStartupSweep()
        {
            RimIgnoreStaging.SweepLeftovers();
        }
    }
}
