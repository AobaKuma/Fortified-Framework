using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace Fortified
{
    /// <summary>
    /// vanilla 的 <see cref="CompUsable.CanBeUsedBy"/> 第一行就以 RaceProps.IsFlesh 擋掉所有非血肉 pawn，
    /// 導致機械體（<see cref="IWeaponUsable"/>）完全無法使用可部署物件的收納功能
    /// —— 浮動選單不會出現，即使強制下 job，CompUsable.UsedBy 內部也會再擋一次而靜默失效。
    ///
    /// 這裡以 transpiler 精準替換那一次 get_IsFlesh 呼叫，並且只對
    /// 「掛有 <see cref="CompMinifyToInventory"/> 的物件」放行，
    /// 避免順手把所有 CompUsable（神經訓練器、藥物……）都對機械體解鎖。
    /// </summary>
    [HarmonyPatch(typeof(CompUsable), nameof(CompUsable.CanBeUsedBy))]
    internal static class Patch_CompUsable_CanBeUsedBy
    {
        private const string LogPrefix = "[FFF] Patch_CompUsable_CanBeUsedBy：";

        private static readonly MethodInfo IsFleshGetter =
            AccessTools.PropertyGetter(typeof(RaceProperties), nameof(RaceProperties.IsFlesh));

        private static readonly MethodInfo ReplacementMethod =
            AccessTools.Method(typeof(Patch_CompUsable_CanBeUsedBy), nameof(IsFleshOrDeployableUser));

        internal static bool Prepare()
        {
            if (IsFleshGetter == null || ReplacementMethod == null)
            {
                Log.Warning(LogPrefix + "找不到 RaceProperties.IsFlesh 或替換方法，略過修補。機械體將無法收納可部署物件。");
                return false;
            }
            return true;
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);

            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(IsFleshGetter))
                {
                    continue;
                }

                // 堆疊上目前是 RaceProperties，補上 this(CompUsable) 與 pawn 後改呼叫自家判定。
                CodeInstruction original = codes[i];
                CodeInstruction replacement = new CodeInstruction(OpCodes.Ldarg_0);
                replacement.labels.AddRange(original.labels);
                replacement.blocks.AddRange(original.blocks);

                codes[i] = replacement;
                codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_1));
                codes.Insert(i + 2, new CodeInstruction(OpCodes.Call, ReplacementMethod));
                return codes;
            }

            // 找不到目標指令（遊戲改版或其他 mod 先行改寫）時原封不動退回，絕不讓 transpiler 產生壞 IL。
            Log.Warning(LogPrefix + "在 CanBeUsedBy 中找不到 RaceProperties.IsFlesh 呼叫，維持原始 IL。機械體將無法收納可部署物件。");
            return codes;
        }

        /// <summary>
        /// 取代 <c>p.RaceProps.IsFlesh</c> 的判定。血肉 pawn 行為完全不變；
        /// 非血肉 pawn 僅在「實作 IWeaponUsable」且「目標物件掛有 CompMinifyToInventory」時放行。
        /// </summary>
        private static bool IsFleshOrDeployableUser(RaceProperties race, CompUsable comp, Pawn pawn)
        {
            try
            {
                if (race != null && race.IsFlesh)
                {
                    return true;
                }
                if (pawn is not IWeaponUsable)
                {
                    return false;
                }
                return comp?.parent?.GetComp<CompMinifyToInventory>() != null;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce(LogPrefix + "判定時發生例外：" + ex, 0x0FF10001);
                return race != null && race.IsFlesh;
            }
        }
    }
}
