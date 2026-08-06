using System;
using System.Collections.Generic;
using System.IO;
using Verse;

namespace Fortified
{
    /// <summary>被排除的單一項目（頂層剪枝點）。目錄項目代表整棵子樹都不會上傳。</summary>
    public sealed class RimIgnoreEntry
    {
        public string RelativePath;
        public bool IsDirectory;
        public long Bytes;
        public int FileCount;
        public RimIgnoreRule Rule;

        public string RuleDescription
        {
            get
            {
                if (Rule == null)
                {
                    return "-";
                }
                return Rule.IsImplicit
                    ? RimIgnoreText.Get(RimIgnoreText.RuleImplicit, "[內建] {0}", Rule.RawLine)
                    : RimIgnoreText.Get(RimIgnoreText.RuleLine, "L{0}: {1}", Rule.LineNumber, Rule.RawLine);
            }
        }
    }

    /// <summary>
    /// 對某個模組資料夾套用 .rimignore 之後的結果快照。
    /// 只做唯讀掃描，不會碰任何檔案；實際的鏡像建置由 <see cref="RimIgnoreStaging"/> 負責。
    /// </summary>
    public sealed class RimIgnorePlan
    {
        /// <summary>單次掃描的最大深度，避免異常結構造成無限遞迴。</summary>
        private const int MaxDepth = 64;

        /// <summary>單次掃描的最大項目數，避免誤指到巨大資料夾時卡死主執行緒。</summary>
        private const int MaxEntries = 300000;

        public DirectoryInfo Root { get; private set; }
        public RimIgnoreSpec Spec { get; private set; }

        /// <summary>被排除的頂層項目（目錄或檔案），依相對路徑排序。</summary>
        public List<RimIgnoreEntry> ExcludedEntries { get; } = new List<RimIgnoreEntry>();

        /// <summary>保留下來、需要建立鏡像的檔案相對路徑。</summary>
        public List<string> IncludedFiles { get; } = new List<string>();

        /// <summary>保留下來的目錄相對路徑（含空目錄，確保鏡像結構一致）。</summary>
        public List<string> IncludedDirectories { get; } = new List<string>();

        public long IncludedBytes { get; private set; }
        public long ExcludedBytes { get; private set; }
        public int ExcludedFileCount { get; private set; }

        public List<string> Warnings { get; } = new List<string>();

        /// <summary>掃描是否因為超出安全上限而提前中止。</summary>
        public bool Truncated { get; private set; }

        public int IncludedFileCount => IncludedFiles.Count;

        public bool HasExclusions => ExcludedEntries.Count > 0;

        private RimIgnorePlan()
        {
        }

        /// <summary>
        /// 掃描 root 並套用 spec。任一參數無效時回傳 null。
        /// 本方法只讀取檔案系統中繼資料，不會修改任何內容。
        /// </summary>
        public static RimIgnorePlan Build(DirectoryInfo root, RimIgnoreSpec spec)
        {
            if (root == null || spec == null)
            {
                return null;
            }
            try
            {
                root.Refresh();
                if (!root.Exists)
                {
                    return null;
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Fortified] 無法存取模組資料夾：{e}");
                return null;
            }

            RimIgnorePlan plan = new RimIgnorePlan { Root = root, Spec = spec };
            plan.Warnings.AddRange(spec.Warnings);

            int visited = 0;
            Stack<Frame> stack = new Stack<Frame>();
            stack.Push(new Frame(root, string.Empty, 0));

            while (stack.Count > 0)
            {
                Frame frame = stack.Pop();

                if (frame.Depth > MaxDepth)
                {
                    plan.Warnings.Add(RimIgnoreText.Get(RimIgnoreText.WarnDepthLimit,
                        "資料夾巢狀超過 {0} 層，已停止深入：{1}", MaxDepth, Describe(frame.RelativePath)));
                    continue;
                }

                FileSystemInfo[] children;
                try
                {
                    children = frame.Directory.GetFileSystemInfos();
                }
                catch (Exception e)
                {
                    plan.Warnings.Add(RimIgnoreText.Get(RimIgnoreText.WarnListDirFailed,
                        "無法列出資料夾內容（{0}）：{1}", Describe(frame.RelativePath), e.Message));
                    continue;
                }

                foreach (FileSystemInfo child in children)
                {
                    if (++visited > MaxEntries)
                    {
                        plan.Truncated = true;
                        plan.Warnings.Add(RimIgnoreText.Get(RimIgnoreText.WarnEntryLimit,
                            "項目數超過 {0}，掃描已提前中止。請確認 {1} 的路徑設定是否正確。", MaxEntries, RimIgnoreSpec.FileName));
                        stack.Clear();
                        break;
                    }

                    string name = child.Name;
                    string relative = frame.RelativePath.Length == 0 ? name : frame.RelativePath + "/" + name;

                    // 暫存鏡像資料夾永遠不參與掃描，且不可被 ! 規則重新納入。
                    if (frame.Depth == 0 && string.Equals(name, RimIgnoreStaging.StagingFolderName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool isDirectory;
                    try
                    {
                        isDirectory = (child.Attributes & FileAttributes.Directory) == FileAttributes.Directory;
                    }
                    catch (Exception e)
                    {
                        plan.Warnings.Add(RimIgnoreText.Get(RimIgnoreText.WarnAttributeFailed,
                            "無法讀取項目屬性（{0}）：{1}", relative, e.Message));
                        continue;
                    }

                    // 連結點 / 符號連結一律跳過：既避免遞迴迴圈，也避免把外部內容拉進上傳包。
                    bool isReparse = false;
                    try
                    {
                        isReparse = (child.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
                    }
                    catch
                    {
                        // 屬性讀取失敗時保守處理為一般項目
                    }
                    if (isReparse)
                    {
                        plan.Warnings.Add(RimIgnoreText.Get(RimIgnoreText.WarnSkippedSymlink,
                            "已跳過符號連結／連結點：{0}", relative));
                        continue;
                    }

                    bool ignored = spec.IsIgnored(relative, isDirectory, out RimIgnoreRule rule);

                    if (ignored)
                    {
                        RimIgnoreEntry entry = new RimIgnoreEntry
                        {
                            RelativePath = relative,
                            IsDirectory = isDirectory,
                            Rule = rule
                        };

                        if (isDirectory)
                        {
                            MeasureDirectory(child as DirectoryInfo, entry, plan);
                        }
                        else
                        {
                            entry.FileCount = 1;
                            entry.Bytes = SafeLength(child as FileInfo);
                        }

                        plan.ExcludedEntries.Add(entry);
                        plan.ExcludedBytes += entry.Bytes;
                        plan.ExcludedFileCount += entry.FileCount;
                        continue;
                    }

                    if (isDirectory)
                    {
                        plan.IncludedDirectories.Add(relative);
                        stack.Push(new Frame((DirectoryInfo)child, relative, frame.Depth + 1));
                    }
                    else
                    {
                        plan.IncludedFiles.Add(relative);
                        plan.IncludedBytes += SafeLength(child as FileInfo);
                    }
                }
            }

            plan.ExcludedEntries.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
            plan.IncludedFiles.Sort(StringComparer.OrdinalIgnoreCase);
            plan.IncludedDirectories.Sort(StringComparer.OrdinalIgnoreCase);
            return plan;
        }

        /// <summary>統計被剪除子樹的檔案數與容量，僅供 UI 顯示，失敗時只記警告。</summary>
        private static void MeasureDirectory(DirectoryInfo dir, RimIgnoreEntry entry, RimIgnorePlan plan)
        {
            if (dir == null)
            {
                return;
            }

            int guard = 0;
            Stack<KeyValuePair<DirectoryInfo, int>> stack = new Stack<KeyValuePair<DirectoryInfo, int>>();
            stack.Push(new KeyValuePair<DirectoryInfo, int>(dir, 0));

            while (stack.Count > 0)
            {
                KeyValuePair<DirectoryInfo, int> pair = stack.Pop();
                if (pair.Value > MaxDepth || ++guard > 50000)
                {
                    return;
                }

                FileSystemInfo[] children;
                try
                {
                    children = pair.Key.GetFileSystemInfos();
                }
                catch
                {
                    continue;
                }

                foreach (FileSystemInfo child in children)
                {
                    try
                    {
                        if ((child.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                        {
                            continue;
                        }
                        if ((child.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                        {
                            stack.Push(new KeyValuePair<DirectoryInfo, int>((DirectoryInfo)child, pair.Value + 1));
                        }
                        else
                        {
                            entry.FileCount++;
                            entry.Bytes += SafeLength(child as FileInfo);
                        }
                    }
                    catch
                    {
                        // 個別項目失敗不影響整體統計
                    }
                }
            }
        }

        private static long SafeLength(FileInfo file)
        {
            if (file == null)
            {
                return 0L;
            }
            try
            {
                return file.Length;
            }
            catch
            {
                return 0L;
            }
        }

        private static string Describe(string relative)
        {
            return relative.NullOrEmpty()
                ? RimIgnoreText.Get(RimIgnoreText.ModRootLabel, "<模組根目錄>")
                : relative;
        }

        /// <summary>把位元組數格式化為人類可讀字串。</summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024L)
            {
                return bytes + " B";
            }
            if (bytes < 1024L * 1024L)
            {
                return (bytes / 1024.0).ToString("F1") + " KB";
            }
            if (bytes < 1024L * 1024L * 1024L)
            {
                return (bytes / (1024.0 * 1024.0)).ToString("F1") + " MB";
            }
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";
        }

        private struct Frame
        {
            public readonly DirectoryInfo Directory;
            public readonly string RelativePath;
            public readonly int Depth;

            public Frame(DirectoryInfo directory, string relativePath, int depth)
            {
                Directory = directory;
                RelativePath = relativePath;
                Depth = depth;
            }
        }
    }
}
