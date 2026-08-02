using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 單一條 .rimignore 規則。語法完全比照 GitHub / Git 的 .gitignore：
    ///   #           註解（\# 可轉義為字面井號）
    ///   !pattern    反選（重新納入），後出現者覆蓋先出現者
    ///   /pattern    錨定於模組根目錄
    ///   pattern/    僅匹配目錄
    ///   *           匹配任意字元但不跨越 /
    ///   ?           匹配單一字元但不跨越 /
    ///   [abc] [!abc] 字元集
    ///   **          跨目錄萬用（**/ 任意深度、a/** 目錄下全部）
    /// 若樣式中間（非結尾）含有 /，則自動視為錨定樣式；否則對任意深度的檔名生效。
    /// </summary>
    public sealed class RimIgnoreRule
    {
        /// <summary>原始行文字（未經處理，供 UI 顯示）。</summary>
        public string RawLine { get; private set; }

        /// <summary>此規則在 .rimignore 內的行號（1 起算）。</summary>
        public int LineNumber { get; private set; }

        /// <summary>是否為 ! 開頭的反選規則。</summary>
        public bool Negate { get; private set; }

        /// <summary>是否僅對目錄生效（樣式以 / 結尾）。</summary>
        public bool DirectoryOnly { get; private set; }

        /// <summary>是否為內建預設規則（非使用者撰寫，UI 會另外標示）。</summary>
        public bool IsImplicit { get; private set; }

        private readonly Regex matcher;

        private RimIgnoreRule(string rawLine, int lineNumber, bool negate, bool directoryOnly, bool isImplicit, Regex matcher)
        {
            RawLine = rawLine;
            LineNumber = lineNumber;
            Negate = negate;
            DirectoryOnly = directoryOnly;
            IsImplicit = isImplicit;
            this.matcher = matcher;
        }

        /// <summary>
        /// 測試相對路徑是否命中此規則。relativePath 必須是以 / 分隔、不含前後斜線的相對路徑。
        /// </summary>
        public bool Matches(string relativePath, bool isDirectory)
        {
            if (matcher == null || relativePath.NullOrEmpty())
            {
                return false;
            }
            if (DirectoryOnly && !isDirectory)
            {
                return false;
            }
            try
            {
                return matcher.IsMatch(relativePath);
            }
            catch (Exception e)
            {
                // Regex 逾時等極端情況：寧可判定為不命中，也不要讓上傳流程崩潰。
                Log.Warning($"[Fortified] .rimignore 規則比對失敗（第 {LineNumber} 行：{RawLine}）：{e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 解析單行。回傳 null 代表該行為空行或註解（不產生規則）。
        /// </summary>
        public static RimIgnoreRule Parse(string line, int lineNumber, bool isImplicit, List<string> warnings)
        {
            if (line == null)
            {
                return null;
            }

            // 去除 BOM 與 CR，Git 亦忽略行首 BOM。
            line = line.Replace("\r", string.Empty).TrimStart('\uFEFF');

            string working = TrimUnescapedTrailingWhitespace(line);
            if (working.Length == 0)
            {
                return null;
            }
            if (working[0] == '#')
            {
                return null;
            }

            bool negate = false;
            if (working[0] == '!')
            {
                negate = true;
                working = working.Substring(1);
                if (working.Length == 0)
                {
                    warnings?.Add(RimIgnoreText.Get(RimIgnoreText.WarnNoPatternAfterBang,
                        "第 {0} 行：'!' 之後沒有樣式，已忽略。", lineNumber));
                    return null;
                }
            }
            else if (working[0] == '\\' && working.Length > 1 && (working[1] == '#' || working[1] == '!'))
            {
                // 轉義的 \# 或 \! 還原為字面字元
                working = working.Substring(1);
            }

            bool directoryOnly = false;
            if (working.Length > 1 && working[working.Length - 1] == '/')
            {
                directoryOnly = true;
                working = working.Substring(0, working.Length - 1);
            }
            else if (working == "/")
            {
                warnings?.Add(RimIgnoreText.Get(RimIgnoreText.WarnSlashOnly,
                    "第 {0} 行：樣式僅有 '/'，已忽略。", lineNumber));
                return null;
            }

            // 統一分隔符，避免使用者寫成 Windows 反斜線路徑。
            // 注意：反斜線在 gitignore 中是轉義字元，因此只在「明顯是路徑分隔」時才轉換。
            working = NormalizeWindowsSeparators(working);

            bool anchored = false;
            if (working.Length > 0 && working[0] == '/')
            {
                anchored = true;
                working = working.TrimStart('/');
            }
            else if (working.IndexOf('/') >= 0)
            {
                // 樣式中間含有 /（結尾斜線已在上面剝除）→ 依 gitignore 規則視為錨定
                anchored = true;
            }

            if (working.Length == 0)
            {
                warnings?.Add(RimIgnoreText.Get(RimIgnoreText.WarnEmptyPattern,
                    "第 {0} 行：樣式為空，已忽略。", lineNumber));
                return null;
            }

            string regexText;
            try
            {
                regexText = TranslateGlob(working, anchored);
            }
            catch (Exception e)
            {
                warnings?.Add(RimIgnoreText.Get(RimIgnoreText.WarnParseFailed,
                    "第 {0} 行：樣式無法解析（{1}），已忽略。", lineNumber, e.Message));
                return null;
            }

            Regex regex;
            try
            {
                // 忽略大小寫：RimWorld 的主要平台（Windows / macOS）檔案系統預設不分大小寫，
                // 這樣的行為比嚴格區分大小寫更符合模組作者的預期。
                regex = new Regex(regexText,
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                    TimeSpan.FromSeconds(2));
            }
            catch (Exception e)
            {
                warnings?.Add(RimIgnoreText.Get(RimIgnoreText.WarnCompileFailed,
                    "第 {0} 行：樣式編譯失敗（{1}），已忽略。", lineNumber, e.Message));
                return null;
            }

            return new RimIgnoreRule(line, lineNumber, negate, directoryOnly, isImplicit, regex);
        }

        /// <summary>去除未被反斜線轉義的行尾空白（比照 gitignore）。</summary>
        private static string TrimUnescapedTrailingWhitespace(string line)
        {
            int end = line.Length;
            while (end > 0 && (line[end - 1] == ' ' || line[end - 1] == '\t'))
            {
                // 計算緊接在前的連續反斜線數量，奇數代表此空白被轉義。
                int backslashes = 0;
                int k = end - 2;
                while (k >= 0 && line[k] == '\\')
                {
                    backslashes++;
                    k--;
                }
                if ((backslashes & 1) == 1)
                {
                    break;
                }
                end--;
            }
            return line.Substring(0, end);
        }

        /// <summary>
        /// 將明顯用作路徑分隔的反斜線轉為 /。
        /// 僅在反斜線後方不是 gitignore 轉義目標字元時才轉換，避免破壞 \* 之類的轉義。
        /// </summary>
        private static string NormalizeWindowsSeparators(string pattern)
        {
            StringBuilder sb = new StringBuilder(pattern.Length);
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                char next = (i + 1 < pattern.Length) ? pattern[i + 1] : '\0';
                bool isEscapeTarget = next == '*' || next == '?' || next == '[' || next == ']'
                    || next == '\\' || next == '!' || next == '#' || next == ' ' || next == '\t';
                if (isEscapeTarget)
                {
                    sb.Append(c);
                    if (next != '\0')
                    {
                        sb.Append(next);
                        i++;
                    }
                }
                else
                {
                    sb.Append('/');
                }
            }
            return sb.ToString();
        }

        /// <summary>將 glob 樣式轉換為正規表達式。pattern 已剝除前導與結尾的 /。</summary>
        private static string TranslateGlob(string pattern, bool anchored)
        {
            StringBuilder sb = new StringBuilder(pattern.Length * 3 + 16);
            sb.Append('^');
            if (!anchored)
            {
                // 未錨定：可出現在任意深度
                sb.Append("(?:.*/)?");
            }

            int i = 0;
            int n = pattern.Length;
            bool atSegmentStart = true;

            while (i < n)
            {
                char c = pattern[i];
                switch (c)
                {
                    case '*':
                    {
                        int starCount = 0;
                        while (i < n && pattern[i] == '*')
                        {
                            starCount++;
                            i++;
                        }
                        bool followedBySlash = i < n && pattern[i] == '/';
                        bool atEnd = i >= n;

                        if (starCount >= 2 && atSegmentStart && followedBySlash)
                        {
                            i++; // 吃掉 '/'
                            sb.Append("(?:[^/]+/)*"); // **/ → 零或多層目錄
                        }
                        else if (starCount >= 2 && atEnd)
                        {
                            sb.Append(".*"); // a/** → a 之下的所有內容
                        }
                        else if (starCount >= 2)
                        {
                            sb.Append(".*"); // 非標準寫法，寬鬆處理
                        }
                        else
                        {
                            sb.Append("[^/]*");
                        }
                        atSegmentStart = false;
                        break;
                    }
                    case '?':
                        sb.Append("[^/]");
                        i++;
                        atSegmentStart = false;
                        break;
                    case '[':
                    {
                        int j = i + 1;
                        bool negateClass = false;
                        if (j < n && (pattern[j] == '!' || pattern[j] == '^'))
                        {
                            negateClass = true;
                            j++;
                        }
                        int contentStart = j;
                        if (j < n && pattern[j] == ']')
                        {
                            j++; // 首位的 ] 視為字面字元
                        }
                        while (j < n && pattern[j] != ']')
                        {
                            j++;
                        }
                        if (j >= n)
                        {
                            // 字元集未閉合 → 當作字面 '['
                            sb.Append("\\[");
                            i++;
                        }
                        else
                        {
                            string inner = pattern.Substring(contentStart, j - contentStart);
                            sb.Append('[');
                            if (negateClass)
                            {
                                sb.Append('^');
                            }
                            foreach (char ic in inner)
                            {
                                if (ic == '\\' || ic == ']' || ic == '^' || ic == '[')
                                {
                                    sb.Append('\\');
                                }
                                sb.Append(ic);
                            }
                            sb.Append(']');
                            i = j + 1;
                        }
                        atSegmentStart = false;
                        break;
                    }
                    case '\\':
                    {
                        i++;
                        if (i < n)
                        {
                            sb.Append(Regex.Escape(pattern[i].ToString()));
                            i++;
                        }
                        else
                        {
                            sb.Append("\\\\");
                        }
                        atSegmentStart = false;
                        break;
                    }
                    case '/':
                        sb.Append('/');
                        i++;
                        atSegmentStart = true;
                        break;
                    default:
                        sb.Append(Regex.Escape(c.ToString()));
                        i++;
                        atSegmentStart = false;
                        break;
                }
            }

            sb.Append('$');
            return sb.ToString();
        }
    }

    /// <summary>
    /// 一整份 .rimignore 的規則集合。比對採 gitignore 的「最後命中者勝出」語意。
    /// </summary>
    public sealed class RimIgnoreSpec
    {
        /// <summary>忽略設定檔的固定檔名。</summary>
        public const string FileName = ".rimignore";

        /// <summary>
        /// 內建預設規則。只在模組確實放置了 .rimignore 時才生效，
        /// 且排在使用者規則之前，因此可用 !pattern 覆寫。
        /// </summary>
        private static readonly string[] ImplicitRules =
        {
            ".rimignore",
            ".git/",
            ".gitattributes",
            ".gitignore",
            ".svn/",
            ".hg/",
            ".vs/",
            ".idea/",
            ".vscode/",
            "Thumbs.db",
            "desktop.ini",
            ".DS_Store"
        };

        private readonly List<RimIgnoreRule> rules = new List<RimIgnoreRule>();

        /// <summary>解析過程中產生的警告訊息，會顯示在預覽視窗上。</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>來源檔案的完整路徑，可能為 null（純程式建立時）。</summary>
        public string SourcePath { get; private set; }

        /// <summary>使用者實際撰寫的有效規則數（不含內建預設）。</summary>
        public int UserRuleCount { get; private set; }

        /// <summary>目前生效的所有規則（唯讀用途）。</summary>
        public IReadOnlyList<RimIgnoreRule> Rules => rules;

        private RimIgnoreSpec()
        {
        }

        /// <summary>從檔案載入。檔案不存在或無法讀取時回傳 null。</summary>
        public static RimIgnoreSpec Load(string path)
        {
            if (path.NullOrEmpty())
            {
                return null;
            }

            string[] lines;
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }
                FileInfo info = new FileInfo(path);
                if (info.Length > 1024L * 1024L)
                {
                    Log.Warning($"[Fortified] {FileName} 超過 1 MB，已略過：{path}");
                    return null;
                }
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (Exception e)
            {
                Log.Error($"[Fortified] 讀取 {FileName} 失敗（{path}）：{e}");
                return null;
            }

            return FromLines(lines, path);
        }

        /// <summary>從文字行建立規則集合。</summary>
        public static RimIgnoreSpec FromLines(IEnumerable<string> lines, string sourcePath = null)
        {
            RimIgnoreSpec spec = new RimIgnoreSpec { SourcePath = sourcePath };

            foreach (string implicitLine in ImplicitRules)
            {
                RimIgnoreRule rule = RimIgnoreRule.Parse(implicitLine, 0, isImplicit: true, warnings: null);
                if (rule != null)
                {
                    spec.rules.Add(rule);
                }
            }

            if (lines != null)
            {
                int lineNumber = 0;
                foreach (string line in lines)
                {
                    lineNumber++;
                    if (lineNumber > 10000)
                    {
                        spec.Warnings.Add(RimIgnoreText.Get(RimIgnoreText.WarnTooManyLines,
                            "{0} 超過 {1} 行，其餘內容已略過。", FileName, 10000));
                        break;
                    }
                    RimIgnoreRule rule = RimIgnoreRule.Parse(line, lineNumber, isImplicit: false, warnings: spec.Warnings);
                    if (rule != null)
                    {
                        spec.rules.Add(rule);
                        spec.UserRuleCount++;
                    }
                }
            }

            return spec;
        }

        /// <summary>
        /// 判斷相對路徑是否應被忽略。matchedRule 回傳最後命中的規則（可能為反選規則）。
        /// </summary>
        public bool IsIgnored(string relativePath, bool isDirectory, out RimIgnoreRule matchedRule)
        {
            matchedRule = null;
            bool ignored = false;

            for (int i = 0; i < rules.Count; i++)
            {
                RimIgnoreRule rule = rules[i];
                if (rule.Matches(relativePath, isDirectory))
                {
                    matchedRule = rule;
                    ignored = !rule.Negate;
                }
            }

            return ignored;
        }

        /// <summary>是否存在任何有效規則（含內建預設）。</summary>
        public bool HasRules => rules.Count > 0;
    }
}
