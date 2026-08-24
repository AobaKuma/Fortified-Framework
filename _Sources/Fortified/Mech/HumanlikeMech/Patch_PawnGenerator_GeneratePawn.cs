// 当白昼倾坠之时
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using Verse;

namespace Fortified
{
    // 在 Pawn 生成后为 HumanlikeMech 添加初始装备
    // 使用 PawnKindDef 中定义的 apparelMoney、apparelTags 和 apparelRequired 配置
    // 参考 RimWorld 的 PawnApparelGenerator 逻辑

    /*#region Patch: GeneratePawn(PawnKindDef, Faction, PlanetTile?)

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn),
        new Type[] { typeof(PawnKindDef), typeof(Faction), typeof(PlanetTile?) })]
    internal static class Patch_PawnGenerator_GeneratePawn
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __result, PawnKindDef kindDef, Faction faction, PlanetTile? tile)
        {
            try { HumanlikeMechApparelPatchHelper.GenerateApparelForHumanlikeMech(__result, kindDef, faction); }
            catch (Exception e) { Log.Error($"[FFF] Patch_PawnGenerator_GeneratePawn Error: {e}"); }
            (__result as IHumanlikeMech)?.ApplyWorkTypeRestrictions();
        }
    }

    #endregion*/ //Removed because this method runs one below anyway

    #region Patch: GeneratePawn(PawnGenerationRequest)

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn),
        new Type[] { typeof(PawnGenerationRequest) })]
    internal static class Patch_PawnGenerator_GeneratePawn_Request
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __result, PawnGenerationRequest request)
        {
            try { HumanlikeMechApparelPatchHelper.GenerateApparelForHumanlikeMech(__result, request.KindDef, request.Faction); }
            catch (Exception e) { Log.Error($"[FFF] Patch_PawnGenerator_GeneratePawn_Request Error: {e}"); }
            (__result as IHumanlikeMech)?.ApplyWorkTypeRestrictions();
        }
    }

    #endregion

    #region 共享逻辑

    internal static class HumanlikeMechApparelPatchHelper
    {
        public static void GenerateApparelForHumanlikeMech(Pawn pawn, PawnKindDef kindDef, Faction faction)
        {
            if(kindDef == null)
            {
                return;
            }

            if(pawn.TryGetComp(out CompMultipleTurretGun comp))
            {
                comp.PostGenInit(pawn);
            }

            if (pawn is not IHumanlikeMech || pawn.apparel == null)
            {
                return;
            }

            // 只为非玩家派系的机兵添加初始装备
            if (faction == null || faction.IsPlayer) return;

            // 检查是否已有装备，避免重复生成
            if (pawn.apparel.WornApparel.Count > 0) return;

            PawnGenerationRequest request = new PawnGenerationRequest(kindDef);
            MechApparelGenerator.GenerateStartingApparelFor(pawn, request);
        }
    }

    #endregion
}
