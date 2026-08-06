// 当白昼倾坠之时
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Fortified
{
    // 瞄准时以手持装备图取代武器绘制
    // 全程自绘, 不经由PawnRenderUtility.DrawEquipmentAiming
    // 原因: CE(Harmony_PawnRenderer_DrawEquipmentAiming)以Transpiler改写该方法的DrawMesh,
    //       缩放改从eq.def.GunDrawExtension/def.graphicData.drawSize取值而非eq.Graphic.drawSize,
    //       且WeaponPlatform分支会丢弃传入material, 使换图与缩放双双失效
    [StaticConstructorOnStartup]
    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAndApparelExtras))]
    public static class Patch_PawnRenderUtility_DrawAimingApparel
    {
        // 接管绘制则跳过原方法
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, Vector3 drawPos, Rot4 facing, PawnRenderFlags flags)
        {
            bool drawn;
            try { drawn = TryDrawAimingApparel(pawn, drawPos, flags); }
            catch (System.Exception e)
            {
                Log.Error($"[FFF] Patch_DrawAimingApparel Error: {e}");
                return true;
            }
            if (!drawn) return true;

            // 已接管武器位, 补跑原版佩戴附加绘制
            try { DrawWornExtras(pawn); }
            catch (System.Exception e) { Log.Error($"[FFF] Patch_DrawAimingApparel WornExtras Error: {e}"); }
            return false;
        }

        // 正用挂comp的apparel瞄准则自绘手持图
        private static bool TryDrawAimingApparel(Pawn pawn, Vector3 drawPos, PawnRenderFlags flags)
        {
            if (flags.HasFlag(PawnRenderFlags.NeverAimWeapon)) return false;
            if (!(pawn.stances?.curStance is Stance_Warmup warmup)) return false;
            if (warmup.neverAimWeapon || !warmup.focusTarg.IsValid) return false;
            if (!(warmup.verb?.EquipmentSource is Apparel apparel)) return false;

            Job curJob = pawn.CurJob;
            if (curJob?.def != null && curJob.def.neverShowWeapon) return false;

            CompAimingEquipmentGraphic comp = apparel.TryGetComp<CompAimingEquipmentGraphic>();
            Graphic graphic = comp?.AimingGraphic;
            if (graphic == null) return false;

            float aimAngle = ResolveAimAngle(pawn, warmup);
            float distFactor = pawn.ageTracker.CurLifeStage.equipmentDrawDistanceFactor;
            Vector3 pos = drawPos
                + new Vector3(0f, 0f, 0.4f + apparel.def.equippedDistanceOffset).RotatedBy(aimAngle) * distFactor;

            DrawAimingGraphic(apparel, graphic, pos, aimAngle);
            return true;
        }

        // 复刻原版DrawEquipmentAiming, 但缩放与材质一律取自comp图
        private static void DrawAimingGraphic(Thing eq, Graphic graphic, Vector3 drawLoc, float aimAngle)
        {
            float angle = aimAngle - 90f;
            Mesh mesh;
            if (aimAngle > 200f && aimAngle < 340f)
            {
                mesh = MeshPool.plane10Flip;
                angle -= 180f;
                angle -= eq.def.equippedAngleOffset;
            }
            else
            {
                mesh = MeshPool.plane10;
                angle += eq.def.equippedAngleOffset;
            }
            angle %= 360f;

            Material material = (graphic is Graphic_StackCount stackGraphic)
                ? stackGraphic.SubGraphicForStackCount(1, eq.def).MatSingleFor(eq)
                : graphic.MatSingleFor(eq);
            if (material == null) return;

            Vector2 size = graphic.drawSize;
            Matrix4x4 matrix = Matrix4x4.TRS(
                drawLoc,
                Quaternion.AngleAxis(angle, Vector3.up),
                new Vector3(size.x, 0f, size.y));
            Graphics.DrawMesh(mesh, matrix, material, 0);
        }

        // 复刻原版佩戴附加绘制
        private static void DrawWornExtras(Pawn pawn)
        {
            if (pawn.apparel == null) return;
            List<Apparel> worn = pawn.apparel.WornApparel;
            if (worn == null) return;
            for (int i = 0; i < worn.Count; i++)
            {
                worn[i].DrawWornExtras();
            }
        }

        // 复刻原版瞄准角计算
        private static float ResolveAimAngle(Pawn pawn, Stance_Busy stance)
        {
            float num = 0f;
            Vector3 target = stance.focusTarg.HasThing
                ? stance.focusTarg.Thing.DrawPos
                : stance.focusTarg.Cell.ToVector3Shifted();
            if ((target - pawn.DrawPos).MagnitudeHorizontalSquared() > 0.001f)
            {
                num = (target - pawn.DrawPos).AngleFlat();
            }
            Verb verb = pawn.CurrentEffectiveVerb;
            if (verb != null && verb.AimAngleOverride.HasValue)
            {
                num = verb.AimAngleOverride.Value;
            }
            return num;
        }
    }

    // 瞄准期间隐藏apparel自身背包图
    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.CanDrawNow))]
    public static class Patch_PawnRenderNodeWorker_HideAimingApparel
    {
        [HarmonyPostfix]
        public static void Postfix(PawnRenderNode node, ref bool __result)
        {
            if (!__result) return;
            if (!(node is PawnRenderNode_Apparel apparelNode)) return;
            Apparel apparel = apparelNode.apparel;
            if (apparel == null) return;
            if (apparel.TryGetComp<CompAimingEquipmentGraphic>() == null) return;
            if (IsAimingWith(apparel)) __result = false;
        }

        // 判穿戴者是否正用本apparel瞄准
        private static bool IsAimingWith(Apparel apparel)
        {
            if (!(apparel.Wearer is Pawn pawn)) return false;
            if (!(pawn.stances?.curStance is Stance_Warmup warmup)) return false;
            return warmup.verb?.EquipmentSource == apparel;
        }
    }
}
