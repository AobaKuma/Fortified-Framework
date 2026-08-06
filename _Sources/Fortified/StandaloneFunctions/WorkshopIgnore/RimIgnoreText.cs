using System;
using Verse;

namespace Fortified
{
    /// <summary>
    /// .rimignore 功能的字串取得入口。
    ///
    /// 所有面向使用者的文字都經過這裡，行為是「有翻譯就用翻譯，沒有就用內建預設值」：
    ///   * 掃描與清理流程可能在語言資料尚未就緒時執行（例如啟動時的殘留掃除），
    ///     直接呼叫 Translate() 會拿到 TRANSLATION MISSING 甚至擲出例外。
    ///   * 佔位符統一以 string.Format 處理，翻譯字串與內建預設值可共用同一組 {0}{1}…，
    ///     缺翻譯時不會退化成半成品文字。
    ///
    /// 這裡的任何失敗都不會往外拋——文字顯示不該有能力中斷上傳流程。
    /// </summary>
    internal static class RimIgnoreText
    {
        /// <summary>
        /// 取得已格式化的字串。key 無對應翻譯時使用 fallback。
        /// </summary>
        internal static string Get(string key, string fallback, params object[] args)
        {
            string template = fallback ?? key ?? string.Empty;

            try
            {
                if (!key.NullOrEmpty()
                    && LanguageDatabase.activeLanguage != null
                    && key.CanTranslate())
                {
                    string translated = key.Translate();
                    if (!translated.NullOrEmpty())
                    {
                        template = translated;
                    }
                }
            }
            catch
            {
                // 語言資料尚未載入或翻譯查找異常 → 沉默退回內建預設值
            }

            if (args == null || args.Length == 0)
            {
                return template;
            }

            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                // 翻譯的佔位符數量與程式不符：回傳未格式化的樣板，總比整段文字消失好。
                return template;
            }
            catch
            {
                return template;
            }
        }

        // ── 鍵名常數 ───────────────────────────────────────────────
        // 集中管理，避免字串散落各處造成打錯字卻無人察覺。

        internal const string Title = "FFF_RimIgnore_Title";
        internal const string Subtitle = "FFF_RimIgnore_Subtitle";
        internal const string Summary = "FFF_RimIgnore_Summary";
        internal const string ColPath = "FFF_RimIgnore_ColPath";
        internal const string ColSize = "FFF_RimIgnore_ColSize";
        internal const string ColRule = "FFF_RimIgnore_ColRule";
        internal const string DirSize = "FFF_RimIgnore_DirSize";
        internal const string NothingExcluded = "FFF_RimIgnore_NothingExcluded";
        internal const string OpenFile = "FFF_RimIgnore_OpenFile";
        internal const string Rescan = "FFF_RimIgnore_Rescan";
        internal const string Confirm = "FFF_RimIgnore_Confirm";
        /// <summary>沿用原版的取消按鈕鍵，所有語言都已有翻譯。</summary>
        internal const string Cancel = "CancelButton";
        internal const string CannotUpload = "FFF_RimIgnore_CannotUpload";
        internal const string RescanFailed = "FFF_RimIgnore_RescanFailed";
        internal const string FileMissing = "FFF_RimIgnore_FileMissing";
        internal const string UploadAnyway = "FFF_RimIgnore_UploadAnyway";
        internal const string ErrorTitle = "FFF_RimIgnore_ErrorTitle";
        internal const string MoreWarnings = "FFF_RimIgnore_MoreWarnings";
        internal const string RuleImplicit = "FFF_RimIgnore_RuleImplicit";
        internal const string RuleLine = "FFF_RimIgnore_RuleLine";
        internal const string ModRootLabel = "FFF_RimIgnore_ModRootLabel";

        internal const string WarnNoPatternAfterBang = "FFF_RimIgnore_Warn_NoPatternAfterBang";
        internal const string WarnSlashOnly = "FFF_RimIgnore_Warn_SlashOnly";
        internal const string WarnEmptyPattern = "FFF_RimIgnore_Warn_EmptyPattern";
        internal const string WarnParseFailed = "FFF_RimIgnore_Warn_ParseFailed";
        internal const string WarnCompileFailed = "FFF_RimIgnore_Warn_CompileFailed";
        internal const string WarnTooManyLines = "FFF_RimIgnore_Warn_TooManyLines";
        internal const string WarnDepthLimit = "FFF_RimIgnore_Warn_DepthLimit";
        internal const string WarnListDirFailed = "FFF_RimIgnore_Warn_ListDirFailed";
        internal const string WarnAttributeFailed = "FFF_RimIgnore_Warn_AttributeFailed";
        internal const string WarnSkippedSymlink = "FFF_RimIgnore_Warn_SkippedSymlink";
        internal const string WarnEntryLimit = "FFF_RimIgnore_Warn_EntryLimit";

        internal const string ErrLoadFailed = "FFF_RimIgnore_Err_LoadFailed";
        internal const string ErrScanFailed = "FFF_RimIgnore_Err_ScanFailed";
        internal const string ErrUnexpected = "FFF_RimIgnore_Err_Unexpected";
        internal const string ErrStagingFailed = "FFF_RimIgnore_Err_StagingFailed";
        internal const string ErrPlanTruncated = "FFF_RimIgnore_Err_PlanTruncated";
        internal const string ErrNothingToUpload = "FFF_RimIgnore_Err_NothingToUpload";
        internal const string ErrMirrorCountMismatch = "FFF_RimIgnore_Err_MirrorCountMismatch";
        internal const string ErrStaleStagingLocked = "FFF_RimIgnore_Err_StaleStagingLocked";

        internal const string DbgNoLocalMods = "FFF_RimIgnore_Dbg_NoLocalMods";
        internal const string DbgAllHaveIgnore = "FFF_RimIgnore_Dbg_AllHaveIgnore";
        internal const string DbgLoadFailed = "FFF_RimIgnore_Dbg_LoadFailed";
        internal const string DbgScanFailed = "FFF_RimIgnore_Dbg_ScanFailed";
        internal const string DbgTemplateCreated = "FFF_RimIgnore_Dbg_TemplateCreated";
        internal const string DbgTemplateFailed = "FFF_RimIgnore_Dbg_TemplateFailed";
        internal const string DbgNoIgnoreSuffix = "FFF_RimIgnore_Dbg_NoIgnoreSuffix";
        internal const string Template = "FFF_RimIgnore_Template";
    }
}
