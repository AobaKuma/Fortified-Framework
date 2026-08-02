using RimWorld;
using System;
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 上傳創意工坊前的排除預覽視窗：列出 .rimignore 將排除的所有項目、命中的規則與節省的容量，
    /// 讓作者在內容真的送出去之前先確認一次。
    /// </summary>
    public class Dialog_RimIgnoreConfirm : Window
    {
        private const float RowHeight = 26f;
        private const float ButtonHeight = 36f;
        private const float SectionGap = 8f;
        private const int MaxWarningsShown = 20;

        private RimIgnorePlan plan;
        private readonly string modName;
        private readonly string ignorePath;
        private readonly Action<RimIgnorePlan> onConfirm;
        private readonly Action onUploadAnyway;

        private Vector2 scrollPosition = Vector2.zero;
        private string cachedSummary;
        private string cachedWarnings;

        public override Vector2 InitialSize => new Vector2(920f, 680f);

        public Dialog_RimIgnoreConfirm(RimIgnorePlan plan, string modName, string ignorePath,
            Action<RimIgnorePlan> onConfirm, Action onUploadAnyway = null)
        {
            this.plan = plan;
            this.modName = modName.NullOrEmpty() ? "?" : modName;
            this.ignorePath = ignorePath;
            this.onConfirm = onConfirm;
            this.onUploadAnyway = onUploadAnyway;

            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnCancel = true;
            doCloseX = true;
            preventCameraMotion = false;

            RebuildCaches();
        }

        private void RebuildCaches()
        {
            if (plan == null)
            {
                cachedSummary = string.Empty;
                cachedWarnings = null;
                return;
            }

            cachedSummary = RimIgnoreText.Get(RimIgnoreText.Summary,
                "即將上傳：{0} 個檔案（{1}）\n\n已排除：{2} 個項目 / {3} 個檔案（{4}）",
                plan.IncludedFileCount.ToString("N0"),
                RimIgnorePlan.FormatBytes(plan.IncludedBytes),
                plan.ExcludedEntries.Count.ToString("N0"),
                plan.ExcludedFileCount.ToString("N0"),
                RimIgnorePlan.FormatBytes(plan.ExcludedBytes));

            if (plan.Warnings.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < plan.Warnings.Count && i < MaxWarningsShown; i++)
                {
                    sb.AppendLine("• " + plan.Warnings[i]);
                }
                if (plan.Warnings.Count > MaxWarningsShown)
                {
                    sb.AppendLine("• " + RimIgnoreText.Get(RimIgnoreText.MoreWarnings,
                        "…（尚有 {0} 則）", plan.Warnings.Count - MaxWarningsShown));
                }
                cachedWarnings = sb.ToString().TrimEndNewlines();
            }
            else
            {
                cachedWarnings = null;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (plan == null)
            {
                Close();
                return;
            }

            float y = inRect.y;

            // 標題
            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(inRect.x, y, inRect.width, 34f);
            Widgets.Label(titleRect, RimIgnoreText.Get(RimIgnoreText.Title, "上傳創意工坊 — 排除內容預覽"));
            y += 36f;

            // 模組名稱與設定檔路徑
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Rect subtitleRect = new Rect(inRect.x, y, inRect.width, 22f);
            Widgets.Label(subtitleRect, RimIgnoreText.Get(RimIgnoreText.Subtitle,
                "{0} — 依 {1} 過濾上傳內容", modName, RimIgnoreSpec.FileName));
            GUI.color = Color.white;
            y += 24f;

            if (!ignorePath.NullOrEmpty())
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Text.Font = GameFont.Tiny;
                Rect pathRect = new Rect(inRect.x, y, inRect.width, 18f);
                Widgets.Label(pathRect, ignorePath);
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                y += 20f;
            }

            y += SectionGap;

            // 統計摘要
            Rect summaryRect = new Rect(inRect.x, y, inRect.width, 46f);
            Widgets.DrawMenuSection(summaryRect);
            Widgets.Label(summaryRect.ContractedBy(8f), cachedSummary);
            y += summaryRect.height + SectionGap;

            // 警告
            if (!cachedWarnings.NullOrEmpty())
            {
                float warnHeight = Mathf.Min(90f, Text.CalcHeight(cachedWarnings, inRect.width - 16f) + 16f);
                Rect warnRect = new Rect(inRect.x, y, inRect.width, warnHeight);
                Widgets.DrawBoxSolid(warnRect, new Color(0.35f, 0.28f, 0.08f, 0.55f));
                GUI.color = new Color(1f, 0.92f, 0.6f);
                Widgets.Label(warnRect.ContractedBy(8f), cachedWarnings);
                GUI.color = Color.white;
                y += warnHeight + SectionGap;
            }

            // 清單標頭
            Rect headerRect = new Rect(inRect.x, y, inRect.width, 22f);
            DrawRowColumns(headerRect,
                RimIgnoreText.Get(RimIgnoreText.ColPath, "項目"),
                RimIgnoreText.Get(RimIgnoreText.ColSize, "大小"),
                RimIgnoreText.Get(RimIgnoreText.ColRule, "命中規則"),
                header: true);
            y += 24f;

            // 排除清單
            float listBottom = inRect.yMax - ButtonHeight - SectionGap * 2f;
            Rect outRect = new Rect(inRect.x, y, inRect.width, Mathf.Max(60f, listBottom - y));
            Rect viewRect = new Rect(0f, 0f, outRect.width - 20f, plan.ExcludedEntries.Count * RowHeight + 4f);

            Widgets.DrawMenuSection(outRect);
            Rect innerOut = outRect.ContractedBy(2f);
            Widgets.BeginScrollView(innerOut, ref scrollPosition, viewRect);
            try
            {
                if (plan.ExcludedEntries.Count == 0)
                {
                    Widgets.Label(new Rect(6f, 4f, viewRect.width - 12f, RowHeight),
                        RimIgnoreText.Get(RimIgnoreText.NothingExcluded, "沒有任何項目被排除。"));
                }
                else
                {
                    float rowY = 2f;
                    for (int i = 0; i < plan.ExcludedEntries.Count; i++)
                    {
                        // 只繪製可視範圍內的列，資料量大時仍保持流暢
                        if (rowY + RowHeight >= scrollPosition.y && rowY <= scrollPosition.y + innerOut.height)
                        {
                            RimIgnoreEntry entry = plan.ExcludedEntries[i];
                            Rect rowRect = new Rect(0f, rowY, viewRect.width, RowHeight);
                            if (i % 2 == 1)
                            {
                                Widgets.DrawAltRect(rowRect);
                            }

                            string label = entry.IsDirectory
                                ? entry.RelativePath + "/"
                                : entry.RelativePath;
                            string size = entry.IsDirectory
                                ? RimIgnoreText.Get(RimIgnoreText.DirSize, "{0} 檔 / {1}",
                                    entry.FileCount.ToString("N0"), RimIgnorePlan.FormatBytes(entry.Bytes))
                                : RimIgnorePlan.FormatBytes(entry.Bytes);

                            DrawRowColumns(rowRect, label, size, entry.RuleDescription, header: false, isDirectory: entry.IsDirectory);
                        }
                        rowY += RowHeight;
                    }
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }

            // 底部按鈕
            float buttonY = inRect.yMax - ButtonHeight;
            float buttonWidth = (inRect.width - SectionGap * 3f) / 4f;
            float x = inRect.x;

            if (Widgets.ButtonText(new Rect(x, buttonY, buttonWidth, ButtonHeight),
                RimIgnoreText.Get(RimIgnoreText.Cancel, "Cancel")))
            {
                Close();
            }
            x += buttonWidth + SectionGap;

            if (Widgets.ButtonText(new Rect(x, buttonY, buttonWidth, ButtonHeight),
                RimIgnoreText.Get(RimIgnoreText.OpenFile, "開啟 .rimignore")))
            {
                OpenIgnoreFile();
            }
            x += buttonWidth + SectionGap;

            if (Widgets.ButtonText(new Rect(x, buttonY, buttonWidth, ButtonHeight),
                RimIgnoreText.Get(RimIgnoreText.Rescan, "重新掃描")))
            {
                Rescan();
            }
            x += buttonWidth + SectionGap;

            bool canUpload = plan.IncludedFileCount > 0 && !plan.Truncated;
            GUI.enabled = canUpload;
            if (Widgets.ButtonText(new Rect(x, buttonY, buttonWidth, ButtonHeight),
                RimIgnoreText.Get(RimIgnoreText.Confirm, "確認上傳")))
            {
                Confirm();
            }
            GUI.enabled = true;

            if (!canUpload)
            {
                TooltipHandler.TipRegion(new Rect(x, buttonY, buttonWidth, ButtonHeight),
                    RimIgnoreText.Get(RimIgnoreText.CannotUpload, "掃描結果不完整，或過濾後沒有任何檔案可上傳。"));
            }

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawRowColumns(Rect rowRect, string path, string size, string rule, bool header, bool isDirectory = false)
        {
            float sizeWidth = 130f;
            float ruleWidth = Mathf.Min(300f, rowRect.width * 0.35f);
            float pathWidth = Mathf.Max(120f, rowRect.width - sizeWidth - ruleWidth - 24f);

            Text.Anchor = TextAnchor.MiddleLeft;
            if (header)
            {
                GUI.color = new Color(0.8f, 0.8f, 0.8f);
            }
            else if (isDirectory)
            {
                GUI.color = new Color(1f, 0.85f, 0.55f);
            }

            Rect pathRect = new Rect(rowRect.x + 6f, rowRect.y, pathWidth, rowRect.height);
            Widgets.Label(pathRect, path);
            if (!header)
            {
                TooltipHandler.TipRegion(pathRect, path);
            }

            GUI.color = header ? new Color(0.8f, 0.8f, 0.8f) : new Color(0.85f, 0.85f, 0.85f);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(pathRect.xMax + 6f, rowRect.y, sizeWidth, rowRect.height), size);

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = header ? new Color(0.8f, 0.8f, 0.8f) : new Color(0.65f, 0.75f, 0.9f);
            Rect ruleRect = new Rect(pathRect.xMax + sizeWidth + 12f, rowRect.y, ruleWidth, rowRect.height);
            Widgets.Label(ruleRect, rule);
            if (!header)
            {
                TooltipHandler.TipRegion(ruleRect, rule);
            }

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void Confirm()
        {
            RimIgnorePlan confirmed = plan;
            Close();
            try
            {
                onConfirm?.Invoke(confirmed);
            }
            catch (Exception e)
            {
                Log.Error("[Fortified] 執行過濾上傳時發生錯誤：" + e);
            }
        }

        private void Rescan()
        {
            try
            {
                RimIgnoreSpec spec = RimIgnoreSpec.Load(ignorePath);
                if (spec == null)
                {
                    Messages.Message(RimIgnoreText.Get(RimIgnoreText.RescanFailed, "重新掃描失敗。"),
                        MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
                RimIgnorePlan rebuilt = RimIgnorePlan.Build(plan.Root, spec);
                if (rebuilt == null)
                {
                    Messages.Message(RimIgnoreText.Get(RimIgnoreText.RescanFailed, "重新掃描失敗。"),
                        MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
                plan = rebuilt;
                scrollPosition = Vector2.zero;
                RebuildCaches();
            }
            catch (Exception e)
            {
                Log.Error("[Fortified] 重新掃描失敗：" + e);
                Messages.Message(RimIgnoreText.Get(RimIgnoreText.RescanFailed, "重新掃描失敗。"),
                    MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        private void OpenIgnoreFile()
        {
            if (ignorePath.NullOrEmpty() || !File.Exists(ignorePath))
            {
                Messages.Message(RimIgnoreText.Get(RimIgnoreText.FileMissing, "找不到 .rimignore。"),
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            try
            {
                Application.OpenURL("file:///" + ignorePath.Replace('\\', '/'));
            }
            catch (Exception e)
            {
                Log.Warning("[Fortified] 無法開啟忽略設定檔：" + e.Message);
            }
        }

        /// <summary>提供 UploadAnyway 分支（保留給錯誤流程重用）。</summary>
        public void InvokeUploadAnyway()
        {
            Close();
            onUploadAnyway?.Invoke();
        }
    }
}
