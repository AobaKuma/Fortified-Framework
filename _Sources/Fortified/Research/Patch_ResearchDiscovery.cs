using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 把 FFF 的「探明」判定接回原生的 <see cref="ResearchProjectDef.IsHidden"/>。
    ///
    /// 之所以選這個接點，是因為原生研究介面本來就已經圍繞 IsHidden 做了一整套隱藏表現：
    /// 節點標籤改為 (未知研究)、使用 HiddenResearchColor、不繪製底列圖示與 tooltip、
    /// 點擊不會選取、CanStartNow 為 false（無法開始研究）、快速搜尋排除、
    /// 書籍的 ReadingOutcomeDoerGainResearch 也不會灌進度。
    /// 直接沿用可省下對 MainTabWindow_Research 的大量 transpiler，遊戲改版時也不易壞。
    ///
    /// 只做 postfix 且僅在原本為 false 時才可能改為 true——不會蓋掉 Anomaly 的實體圖鑑隱藏。
    /// </summary>
    [HarmonyPatch(typeof(ResearchProjectDef), nameof(ResearchProjectDef.IsHidden), MethodType.Getter)]
    public static class Patch_ResearchProjectDef_IsHidden
    {
        [HarmonyPostfix]
        public static void Postfix(ResearchProjectDef __instance, ref bool __result)
        {
            // 原生已判定為隱藏就沒有再算一次的必要（本 getter 是每 frame × 每專案的熱路徑）。
            if (__result)
            {
                return;
            }
            // IsUndiscovered 內部已做重入保護與 try/catch，任何失敗都回傳 false。
            if (ResearchDiscoveryUtility.IsUndiscovered(__instance))
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// 研究資訊面板中，原生直接寫出其他專案名稱與其解鎖物的地方不會經過 IsHidden，
    /// 未探明的專案會在這裡被劇透。以下補丁把它們遮蔽成佔位文字：
    ///   - 「與 X 一同解鎖：」標題中的未探明專案 → [一項未探明的科技]
    ///   - 該群組底下的解鎖物 → [未探明]（佔位圖示、灰字、不可點擊）
    ///   - 「前置研究：」清單中的未探明專案 → [一項未探明的科技]
    ///   - 快速搜尋不會再透過被遮蔽的解鎖物命中專案
    /// 探明後立即恢復原樣（每 frame 重新判定，不依賴研究頁的快取）。
    /// </summary>
    public static class ResearchDiscoveryMasking
    {
        // 與原生 MainTabWindow_Research.MissingPrerequisiteColor 相同。
        private static readonly Color MissingPrerequisiteColor = ColorLibrary.RedReadable;

        public static string UndiscoveredProjectLabel => "FFF_Research_UndiscoveredProject".Translate();

        public static string UndiscoveredUnlockLabel => "FFF_Research_UndiscoveredUnlock".Translate();

        /// <summary>
        /// 此解鎖物是否應遮蔽：只要有任一未探明專案會解鎖它就遮蔽。
        /// 研究面板只能選取已探明的專案，所以「有未探明專案解鎖它」等同於「它所在群組的標題含有未探明專案」。
        /// </summary>
        public static bool IsUnlockMasked(Def def)
        {
            if (def == null)
            {
                return false;
            }
            try
            {
                List<ResearchProjectDef> managed = ResearchDiscoveryUtility.ManagedProjects;
                for (int i = 0; i < managed.Count; i++)
                {
                    ResearchProjectDef proj = managed[i];
                    if (ResearchDiscoveryUtility.IsUndiscovered(proj) && proj.UnlockedDefs.Contains(def))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[FFF] ResearchDiscoveryMasking.IsUnlockMasked failed for {def.defName}: {ex}", 0x4F46F1A);
            }
            return false;
        }

        /// <summary>取代 DrawResearchPrerequisites 中的 Def.LabelCap。</summary>
        public static TaggedString PrerequisiteLabelCap(Def def)
        {
            if (def is ResearchProjectDef proj && ResearchDiscoveryUtility.IsUndiscovered(proj))
            {
                return UndiscoveredProjectLabel;
            }
            return def.LabelCap;
        }

        /// <summary>取代 DrawUnlockableHyperlinks 中的 Widgets.HyperlinkWithIcon（簽章必須與原方法一致）。</summary>
        public static void HyperlinkWithIcon(Rect rect, Dialog_InfoCard.Hyperlink hyperlink, string text, float iconMargin,
            float textOffsetX, Color? color, bool truncateLabel, string textSuffix)
        {
            if (!IsUnlockMasked(hyperlink.def))
            {
                Widgets.HyperlinkWithIcon(rect, hyperlink, text, iconMargin, textOffsetX, color, truncateLabel, textSuffix);
                return;
            }

            // 與原生相同的版面，但不畫真正的圖示、不附加後綴、不可點擊。
            Widgets.BeginGroup(rect);
            Rect iconRect = new Rect(0f, 0f, rect.height, rect.height);
            if (iconMargin != 0f)
            {
                iconRect = iconRect.ContractedBy(iconMargin);
            }
            Widgets.DrawTextureFitted(iconRect, Widgets.PlaceholderIconTex, 1f);
            Rect labelRect = new Rect(iconRect.xMax + textOffsetX, 0f, rect.width - (iconRect.xMax + textOffsetX), rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            GUI.color = Color.gray;
            Widgets.Label(labelRect, UndiscoveredUnlockLabel);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            Widgets.EndGroup();
        }

        /// <summary>把目標方法中對 <paramref name="from"/> 任一者的呼叫改為呼叫 <paramref name="to"/>。找不到時記錄警告並原樣返回。</summary>
        public static IEnumerable<CodeInstruction> ReplaceCall(IEnumerable<CodeInstruction> instructions, MethodInfo to, string context, params MethodInfo[] from)
        {
            bool found = false;
            foreach (CodeInstruction ins in instructions)
            {
                if (from.Any(m => m != null && ins.Calls(m)))
                {
                    ins.opcode = OpCodes.Call;
                    ins.operand = to;
                    found = true;
                }
                yield return ins;
            }
            if (!found)
            {
                Log.Warning($"[FFF] {context}: target call not found; undiscovered research may be shown unmasked there.");
            }
        }

        internal static string BuildHeaderLabel(ResearchPrerequisitesUtility.UnlockedHeader header)
        {
            StringBuilder sb = new StringBuilder();
            string sep = "";
            bool placeholderAdded = false;
            for (int i = 0; i < header.unlockedBy.Count; i++)
            {
                ResearchProjectDef proj = header.unlockedBy[i];
                string text;
                if (ResearchDiscoveryUtility.IsUndiscovered(proj))
                {
                    // 多個未探明專案只顯示一次佔位，避免透露數量。
                    if (placeholderAdded)
                    {
                        continue;
                    }
                    placeholderAdded = true;
                    text = UndiscoveredProjectLabel.Colorize(MissingPrerequisiteColor);
                }
                else
                {
                    text = proj.LabelCap;
                    if (!proj.IsFinished)
                    {
                        text = text.Colorize(MissingPrerequisiteColor);
                    }
                }
                sb.Append(sep).Append(text);
                sep = ", ";
            }
            return sb.ToString();
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_Research), "HeaderLabel")]
    public static class Patch_MainTabWindow_Research_HeaderLabel
    {
        [HarmonyPrefix]
        public static bool Prefix(ResearchPrerequisitesUtility.UnlockedHeader headerProject, ref string __result)
        {
            if (headerProject?.unlockedBy == null || !headerProject.unlockedBy.Any(ResearchDiscoveryUtility.IsUndiscovered))
            {
                return true;
            }
            __result = ResearchDiscoveryMasking.BuildHeaderLabel(headerProject);
            return false;
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_Research), "DrawUnlockableHyperlinks")]
    public static class Patch_MainTabWindow_Research_DrawUnlockableHyperlinks
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ResearchDiscoveryMasking.ReplaceCall(instructions,
                AccessTools.Method(typeof(ResearchDiscoveryMasking), nameof(ResearchDiscoveryMasking.HyperlinkWithIcon)),
                "DrawUnlockableHyperlinks",
                AccessTools.Method(typeof(Widgets), nameof(Widgets.HyperlinkWithIcon)));
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_Research), "DrawResearchPrerequisites")]
    public static class Patch_MainTabWindow_Research_DrawResearchPrerequisites
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // ResearchProjectDef 覆寫了 LabelCap，編譯器可能引用任一層的 getter，兩者都要比對。
            return ResearchDiscoveryMasking.ReplaceCall(instructions,
                AccessTools.Method(typeof(ResearchDiscoveryMasking), nameof(ResearchDiscoveryMasking.PrerequisiteLabelCap)),
                "DrawResearchPrerequisites",
                AccessTools.PropertyGetter(typeof(Def), nameof(Def.LabelCap)),
                AccessTools.PropertyGetter(typeof(ResearchProjectDef), nameof(ResearchProjectDef.LabelCap)));
        }
    }

    /// <summary>快速搜尋：被遮蔽的解鎖物不應讓搜尋命中（否則輸入物品名稱就能找出哪個專案與未探明科技相關）。</summary>
    [HarmonyPatch(typeof(MainTabWindow_Research), "MatchesUnlockedDef")]
    public static class Patch_MainTabWindow_Research_MatchesUnlockedDef
    {
        [HarmonyPrefix]
        public static bool Prefix(Def unlocked, ref bool __result)
        {
            if (!ResearchDiscoveryMasking.IsUnlockMasked(unlocked))
            {
                return true;
            }
            __result = false;
            return false;
        }
    }
}
