using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Fortified
{
    // 原版 CaravanBonusUtility.HasCaravanBonus 把商隊速度加成鎖在 Humanlike 上：
    //
    //     if (pawn.RaceProps.Humanlike && !pawn.Downed)
    //         return pawn.GetStatValue(StatDefOf.CaravanBonusSpeedFactor) > 1f;
    //     return false;
    //
    // 結果任何 ToolUser 機體（機兵、無人機、載具 pawn）即使在 statBases 填了
    // CaravanBonusSpeedFactor 也完全不生效——對「專職後勤運輸機兵」這種設計來說
    // 是個沉默的死值，作者往往要等到實測商隊速度才會發現。
    //
    // 這裡用 postfix 放寬判定：只要不是人形、沒倒地、且該 stat 被明確設在 1 以上就算數。
    // 由於 CaravanBonusSpeedFactor 的 defaultBaseValue 就是 1，這等同於「有填才生效」，
    // 不會誤傷任何沒有主動設定此 stat 的 race。
    //
    // 生效範圍：CaravanTicksPerMoveUtility.GetTicksPerMove() 的世界地圖移動速度計算。
    // 注意該處取的是「有加成成員的平均值」而非全隊平均，因此單一成員的高數值
    // 會直接放大整支商隊的速度，填值請保守（1.1 ~ 1.5 量級）。
    //
    // 已知未修復：TransferableUIUtility.DoExtraIcons() 中
    // `else if (ModsConfig.BiotechActive && pawn.IsColonyMech)` 分支排在
    // HasCaravanBonus 之前，所以殖民地機兵不會顯示商隊加成圖示。
    // 那是純外觀問題，速度計算不受影響，不值得為此再插一個 UI patch。
    [HarmonyPatch(typeof(CaravanBonusUtility), nameof(CaravanBonusUtility.HasCaravanBonus))]
    public static class Patch_CaravanBonusUtility_HasCaravanBonus
    {
        [HarmonyPostfix]
        static void Postfix(Pawn pawn, ref bool __result)
        {
            // 原版已判定為 true，或 pawn 無效：不介入
            if (__result || pawn == null) return;
            // 人形交給原版邏輯，避免重複判定
            if (pawn.RaceProps == null || pawn.RaceProps.Humanlike) return;
            if (pawn.Dead || pawn.Downed) return;

            if (pawn.GetStatValue(StatDefOf.CaravanBonusSpeedFactor) > 1f)
            {
                __result = true;
            }
        }
    }
}
