using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Verse;

namespace Fortified
{
    /// <summary>一次「已過濾上傳」的作業狀態。</summary>
    public sealed class RimIgnoreSession
    {
        public string ModRootFullPath;
        public string ModName;
        public DirectoryInfo StagingDirectory;
        public RimIgnorePlan Plan;
        public int LinkedCount;
        public int CopiedCount;

        /// <summary>RimWorld 是否已實際取用鏡像路徑（用於除錯與記錄）。</summary>
        public bool Redirected;
    }

    /// <summary>
    /// 建立「只含應上傳內容」的暫存鏡像資料夾。
    ///
    /// Steam 的 SteamUGC.SetItemContent 只能指定一個資料夾，無法逐檔排除，
    /// 因此作法是：在模組資料夾底下建立 .fff_ws_staging，
    /// 用 NTFS 硬連結（失敗時退回複製）鏡射所有保留檔案，再把上傳內容路徑導向它。
    ///
    /// 硬連結不佔額外空間、不動到原始檔案；刪除鏡像只會刪除連結本身。
    /// 之所以放在模組資料夾內而非系統暫存資料夾，是為了保證與來源同一個磁碟區，
    /// 讓硬連結一定可用；此資料夾永遠被排除在鏡像之外，RimWorld 也不會將其視為模組。
    /// </summary>
    public static class RimIgnoreStaging
    {
        /// <summary>暫存鏡像資料夾名稱。此名稱同時是刪除操作的安全鎖。</summary>
        public const string StagingFolderName = ".fff_ws_staging";

        private static bool hardLinkApiUnavailable;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        /// <summary>
        /// 依 plan 建立鏡像。任何一個檔案無法鏡射都會擲出例外並自動清除半成品，
        /// 寧可讓上傳中止，也不要送出內容殘缺的版本。
        /// </summary>
        public static RimIgnoreSession Build(DirectoryInfo modRoot, RimIgnorePlan plan, string modName)
        {
            if (modRoot == null)
            {
                throw new ArgumentNullException(nameof(modRoot));
            }
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            if (plan.Truncated)
            {
                throw new InvalidOperationException(RimIgnoreText.Get(RimIgnoreText.ErrPlanTruncated,
                    "掃描結果不完整（超出安全上限），已中止建立鏡像。"));
            }
            if (plan.IncludedFiles.Count == 0)
            {
                throw new InvalidOperationException(RimIgnoreText.Get(RimIgnoreText.ErrNothingToUpload,
                    "套用 {0} 之後沒有任何檔案會被上傳，已中止。", RimIgnoreSpec.FileName));
            }

            string stagingPath = Path.Combine(modRoot.FullName, StagingFolderName);

            // 先清掉上一次殘留的鏡像，確保不會混入舊檔案。
            DeleteStagingDirectory(stagingPath, throwOnFailure: true);

            RimIgnoreSession session = new RimIgnoreSession
            {
                ModRootFullPath = modRoot.FullName,
                ModName = modName,
                Plan = plan
            };

            try
            {
                Directory.CreateDirectory(stagingPath);
                TryHideDirectory(stagingPath);

                foreach (string relativeDir in plan.IncludedDirectories)
                {
                    Directory.CreateDirectory(Path.Combine(stagingPath, ToNativePath(relativeDir)));
                }

                foreach (string relativeFile in plan.IncludedFiles)
                {
                    string native = ToNativePath(relativeFile);
                    string source = Path.Combine(modRoot.FullName, native);
                    string destination = Path.Combine(stagingPath, native);

                    string destinationDir = Path.GetDirectoryName(destination);
                    if (!destinationDir.NullOrEmpty())
                    {
                        Directory.CreateDirectory(destinationDir);
                    }

                    if (TryCreateHardLink(destination, source))
                    {
                        session.LinkedCount++;
                    }
                    else
                    {
                        File.Copy(source, destination, overwrite: true);
                        session.CopiedCount++;
                    }
                }

                int mirrored = CountFiles(stagingPath);
                if (mirrored != plan.IncludedFiles.Count)
                {
                    throw new IOException(RimIgnoreText.Get(RimIgnoreText.ErrMirrorCountMismatch,
                        "鏡像檔案數不符（預期 {0}、實得 {1}）。", plan.IncludedFiles.Count, mirrored));
                }

                session.StagingDirectory = new DirectoryInfo(stagingPath);
                return session;
            }
            catch
            {
                // 建置失敗：清掉半成品再往外拋，不留垃圾。
                DeleteStagingDirectory(stagingPath, throwOnFailure: false);
                throw;
            }
        }

        /// <summary>
        /// 把 About/PublishedFileId.txt 由原始資料夾同步到鏡像。
        /// 首次發佈時該檔案是在 Steam 回呼中才寫入的，必須在導向前補上。
        /// </summary>
        public static void SyncPublishedFileId(RimIgnoreSession session)
        {
            if (session?.StagingDirectory == null || session.ModRootFullPath.NullOrEmpty())
            {
                return;
            }
            try
            {
                string relative = Path.Combine("About", "PublishedFileId.txt");
                string source = Path.Combine(session.ModRootFullPath, relative);
                if (!File.Exists(source))
                {
                    return;
                }
                string destination = Path.Combine(session.StagingDirectory.FullName, relative);
                if (File.Exists(destination) && FilesAreSame(source, destination))
                {
                    return;
                }
                string destinationDir = Path.GetDirectoryName(destination);
                if (!destinationDir.NullOrEmpty())
                {
                    Directory.CreateDirectory(destinationDir);
                }
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
                File.Copy(source, destination, overwrite: true);
            }
            catch (Exception e)
            {
                Log.Warning($"[Fortified] 同步 PublishedFileId.txt 到暫存鏡像失敗：{e.Message}");
            }
        }

        /// <summary>刪除某個 session 的鏡像；先在主執行緒試一次，失敗則背景重試。</summary>
        public static void Cleanup(RimIgnoreSession session)
        {
            if (session?.StagingDirectory == null)
            {
                return;
            }
            string path = session.StagingDirectory.FullName;

            if (DeleteStagingDirectory(path, throwOnFailure: false))
            {
                return;
            }

            // 上傳剛結束時 Steam 可能仍持有檔案控制代碼，改用背景重試。
            try
            {
                Thread thread = new Thread(() =>
                {
                    for (int i = 0; i < 10; i++)
                    {
                        try
                        {
                            Thread.Sleep(2000);
                            if (DeleteStagingDirectory(path, throwOnFailure: false))
                            {
                                return;
                            }
                        }
                        catch
                        {
                            // 背景執行緒不記錄，殘留的鏡像會在下次啟動時被掃除。
                        }
                    }
                })
                {
                    IsBackground = true,
                    Name = "FFF_RimIgnoreCleanup"
                };
                thread.Start();
            }
            catch (Exception e)
            {
                Log.Warning($"[Fortified] 無法啟動暫存鏡像清理執行緒：{e.Message}");
            }
        }

        /// <summary>
        /// 安全地刪除鏡像資料夾。路徑必須以 <see cref="StagingFolderName"/> 結尾，
        /// 這是防止任何情況下誤刪模組本體的最後一道鎖。
        /// </summary>
        public static bool DeleteStagingDirectory(string path, bool throwOnFailure)
        {
            if (path.NullOrEmpty())
            {
                return true;
            }

            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string leaf = Path.GetFileName(trimmed);
            if (!string.Equals(leaf, StagingFolderName, StringComparison.OrdinalIgnoreCase))
            {
                string message = $"[Fortified] 拒絕刪除非暫存鏡像路徑：{path}";
                if (throwOnFailure)
                {
                    throw new InvalidOperationException(message);
                }
                Log.Error(message);
                return false;
            }

            try
            {
                if (!Directory.Exists(trimmed))
                {
                    return true;
                }
                ClearReadOnlyRecursive(new DirectoryInfo(trimmed), 0);
                Directory.Delete(trimmed, recursive: true);
                return !Directory.Exists(trimmed);
            }
            catch (Exception e)
            {
                if (throwOnFailure)
                {
                    throw new IOException(RimIgnoreText.Get(RimIgnoreText.ErrStaleStagingLocked,
                        "無法清除既有的暫存鏡像：{0}", trimmed), e);
                }
                return false;
            }
        }

        /// <summary>遊戲啟動時掃除所有本機模組裡殘留的鏡像資料夾。</summary>
        public static void SweepLeftovers()
        {
            try
            {
                foreach (ModMetaData mod in ModLister.AllInstalledMods)
                {
                    try
                    {
                        DirectoryInfo root = mod?.RootDir;
                        if (root == null || !root.Exists)
                        {
                            continue;
                        }
                        string stagingPath = Path.Combine(root.FullName, StagingFolderName);
                        if (!Directory.Exists(stagingPath))
                        {
                            continue;
                        }
                        if (DeleteStagingDirectory(stagingPath, throwOnFailure: false))
                        {
                            Log.Message($"[Fortified] 已清除殘留的上傳暫存鏡像：{stagingPath}");
                        }
                        else
                        {
                            Log.Warning($"[Fortified] 殘留的上傳暫存鏡像無法刪除，請手動移除：{stagingPath}");
                        }
                    }
                    catch (Exception inner)
                    {
                        Log.Warning($"[Fortified] 掃除暫存鏡像時略過一個模組：{inner.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[Fortified] 暫存鏡像掃除程序失敗：{e.Message}");
            }
        }

        private static bool TryCreateHardLink(string destination, string source)
        {
            if (hardLinkApiUnavailable)
            {
                return false;
            }
            try
            {
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
                return CreateHardLinkW(destination, source, IntPtr.Zero);
            }
            catch (DllNotFoundException)
            {
                hardLinkApiUnavailable = true; // 非 Windows 平台
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                hardLinkApiUnavailable = true;
                return false;
            }
            catch (Exception)
            {
                return false; // 個別檔案失敗 → 退回複製
            }
        }

        private static void TryHideDirectory(string path)
        {
            try
            {
                DirectoryInfo info = new DirectoryInfo(path);
                info.Attributes |= FileAttributes.Hidden;
            }
            catch
            {
                // 純美化用途，失敗無妨
            }
        }

        private static void ClearReadOnlyRecursive(DirectoryInfo dir, int depth)
        {
            if (dir == null || depth > 64)
            {
                return;
            }
            try
            {
                foreach (FileInfo file in dir.GetFiles())
                {
                    try
                    {
                        if ((file.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                        {
                            file.Attributes &= ~FileAttributes.ReadOnly;
                        }
                    }
                    catch
                    {
                        // 略過個別檔案
                    }
                }
                foreach (DirectoryInfo sub in dir.GetDirectories())
                {
                    ClearReadOnlyRecursive(sub, depth + 1);
                }
            }
            catch
            {
                // 略過無法列舉的層級
            }
        }

        private static int CountFiles(string path)
        {
            try
            {
                return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length;
            }
            catch
            {
                return -1;
            }
        }

        private static bool FilesAreSame(string a, string b)
        {
            try
            {
                FileInfo fa = new FileInfo(a);
                FileInfo fb = new FileInfo(b);
                return fa.Length == fb.Length && fa.LastWriteTimeUtc == fb.LastWriteTimeUtc;
            }
            catch
            {
                return false;
            }
        }

        private static string ToNativePath(string relativePath)
        {
            return relativePath.Replace('/', Path.DirectorySeparatorChar);
        }
    }
}
