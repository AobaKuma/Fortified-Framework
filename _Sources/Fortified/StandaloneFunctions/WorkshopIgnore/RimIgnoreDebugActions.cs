using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 開發者模式下的 .rimignore 輔助工具：不需要真的按下上傳，就能預覽排除結果或產生範本。
    /// </summary>
    public static class RimIgnoreDebugActions
    {
        // DebugAction 的標籤必須是編譯期常數，無法走翻譯系統，因此採雙語標示。
        [DebugAction("Fortified", "Preview .rimignore exclusions / 預覽排除結果…",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Entry | AllowedGameStates.PlayingOnMap)]
        private static void PreviewRimIgnore()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (ModMetaData mod in EnumerateLocalMods())
            {
                ModMetaData captured = mod;
                string ignorePath = Path.Combine(captured.RootDir.FullName, RimIgnoreSpec.FileName);
                bool hasIgnore = File.Exists(ignorePath);

                string label = hasIgnore
                    ? captured.Name
                    : captured.Name + RimIgnoreText.Get(RimIgnoreText.DbgNoIgnoreSuffix,
                        "（無 {0}）", RimIgnoreSpec.FileName);

                options.Add(new FloatMenuOption(label, () => ShowPreview(captured, ignorePath, hasIgnore)));
            }

            if (options.Count == 0)
            {
                Messages.Message(RimIgnoreText.Get(RimIgnoreText.DbgNoLocalMods, "找不到任何本機模組資料夾。"),
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        [DebugAction("Fortified", "Create .rimignore template / 建立範本…",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.Entry | AllowedGameStates.PlayingOnMap)]
        private static void CreateRimIgnoreTemplate()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (ModMetaData mod in EnumerateLocalMods())
            {
                ModMetaData captured = mod;
                string ignorePath = Path.Combine(captured.RootDir.FullName, RimIgnoreSpec.FileName);
                if (File.Exists(ignorePath))
                {
                    continue; // 已存在就不覆寫，避免蓋掉使用者的設定
                }
                options.Add(new FloatMenuOption(captured.Name, () => WriteTemplate(ignorePath)));
            }

            if (options.Count == 0)
            {
                Messages.Message(RimIgnoreText.Get(RimIgnoreText.DbgAllHaveIgnore,
                        "所有本機模組都已經有 {0} 了。", RimIgnoreSpec.FileName),
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static IEnumerable<ModMetaData> EnumerateLocalMods()
        {
            List<ModMetaData> result = new List<ModMetaData>();
            try
            {
                foreach (ModMetaData mod in ModLister.AllInstalledMods)
                {
                    try
                    {
                        if (mod == null || mod.Official || mod.OnSteamWorkshop)
                        {
                            continue;
                        }
                        if (mod.RootDir == null || !mod.RootDir.Exists)
                        {
                            continue;
                        }
                        result.Add(mod);
                    }
                    catch
                    {
                        // 略過個別異常模組
                    }
                }
                result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception e)
            {
                Log.Error("[Fortified] 列舉本機模組失敗：" + e);
            }
            return result;
        }

        private static void ShowPreview(ModMetaData mod, string ignorePath, bool hasIgnore)
        {
            try
            {
                RimIgnoreSpec spec = hasIgnore
                    ? RimIgnoreSpec.Load(ignorePath)
                    : RimIgnoreSpec.FromLines(null, ignorePath);

                if (spec == null)
                {
                    Messages.Message(RimIgnoreText.Get(RimIgnoreText.DbgLoadFailed,
                            "無法讀取 {0}。", RimIgnoreSpec.FileName),
                        MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }

                RimIgnorePlan plan = RimIgnorePlan.Build(mod.RootDir, spec);
                if (plan == null)
                {
                    Messages.Message(RimIgnoreText.Get(RimIgnoreText.DbgScanFailed, "無法掃描模組資料夾。"),
                        MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }

                // 預覽模式：確認鍵不會真的上傳。
                Find.WindowStack.Add(new Dialog_RimIgnoreConfirm(plan, mod.Name, ignorePath, null));
            }
            catch (Exception e)
            {
                Log.Error("[Fortified] 預覽 " + RimIgnoreSpec.FileName + " 失敗：" + e);
            }
        }

        private static void WriteTemplate(string ignorePath)
        {
            try
            {
                string text = RimIgnoreText.Get(RimIgnoreText.Template, FallbackTemplateText);

                // 統一為 CRLF：這個檔案主要在 Windows 上用記事本之類的編輯器開啟。
                text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");

                File.WriteAllText(ignorePath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                Messages.Message(RimIgnoreText.Get(RimIgnoreText.DbgTemplateCreated, "已建立 {0}", ignorePath),
                    MessageTypeDefOf.TaskCompletion, historical: false);
                Log.Message("[Fortified] 已建立 " + RimIgnoreSpec.FileName + " 範本：" + ignorePath);
            }
            catch (Exception e)
            {
                Log.Error("[Fortified] 建立 " + RimIgnoreSpec.FileName + " 範本失敗：" + e);
                Messages.Message(RimIgnoreText.Get(RimIgnoreText.DbgTemplateFailed, "建立失敗，詳情請見主控台。"),
                    MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        /// <summary>
        /// 缺少 FFF_RimIgnore_Template 翻譯時使用的內建範本。
        /// 各語言可在 Keyed/WorkshopIgnore.xml 覆寫成當地語言版本。
        /// </summary>
        private const string FallbackTemplateText =
@"# .rimignore — 上傳 Steam 創意工坊時要排除的檔案／資料夾
#
# 語法與 GitHub 的 .gitignore 完全相同：
#   #        註解（\# 可轉義為字面井號）
#   !xxx     反選（重新納入），後出現的規則覆蓋先出現的規則
#   /xxx     錨定於模組根目錄
#   xxx/     僅匹配目錄
#   *        任意字元（不跨越 /）
#   ?        單一字元（不跨越 /）
#   [abc]    字元集，[!abc] 為排除
#   **/xxx   任意深度；xxx/** 代表該目錄底下全部
#
# 注意：與 Git 相同，資料夾一旦被排除，其中的內容就無法再用 ! 重新納入。
# 內建已自動排除：本檔案、.git/ .gitignore .gitattributes .svn/ .hg/
#                 .vs/ .idea/ .vscode/ Thumbs.db desktop.ini .DS_Store
# 這些內建規則同樣可以用 ! 覆寫，例如：!.gitignore

# ── 原始碼與建置產物 ──
/_Sources/
/Source/
**/obj/
**/bin/
*.pdb
*.csproj.user
*.sln
*.suo

# ── Unity 專案 ──
/UnityProject/

# ── 開發用工具與捷徑 ──
/_Tools/
*.lnk
*.bat

# ── 編輯器與備份雜訊 ──
*.orig
*.rej
*.bak
*~

# ── 範例：排除整個資料夾但保留其中一個檔案 ──
# 先排除「內容」而非資料夾本身，再反選要保留的檔案：
# /Docs/*
# !/Docs/Manual.md
";
    }
}
