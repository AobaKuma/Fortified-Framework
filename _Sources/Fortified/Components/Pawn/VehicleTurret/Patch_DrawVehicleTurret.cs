using Verse;
using UnityEngine;
using HarmonyLib;
using RimWorld;

namespace Fortified
{
    [HarmonyPatch(typeof(PawnRenderNodeWorker_Carried), nameof(PawnRenderNodeWorker_Carried.CanDrawNow))]
    internal static class Patch_PawnRenderNodeWorker_Carried_CanDrawNow
    {
        public static void Postfix(ref bool __result, PawnRenderNode node, PawnDrawParms parms)
        {
            if (__result) return;
            if (parms.pawn.equipment?.Primary != null && parms.pawn.HasComp<CompVehicleWeapon>())
            {
                __result = true;
            }
        }
    }
    [HarmonyPatch(typeof(PawnRenderNodeWorker_Carried), nameof(PawnRenderNodeWorker_Carried.PostDraw))]
    internal static class Patch_PawnRenderNodeWorker_Carried
    {
        [HarmonyPriority(600)]
        public static void Postfix(PawnRenderNode node, PawnDrawParms parms, Mesh mesh, Matrix4x4 matrix)
        {
            CompVehicleWeapon compWeapon = CompVehicleWeapon.cachedVehicldesPawns.TryGetValue(parms.pawn);
            if (compWeapon == null) return;
            if (parms.pawn.equipment != null && parms.pawn.equipment.Primary != null)
            {
                DrawTuret(parms.pawn, compWeapon, parms.pawn.equipment.Primary, GetAnimationOffset(node, parms));
            }
        }
        /// <summary>
        /// Sum of the AnimationDef offsets on this carried node and every ancestor (e.g. a "Root" key
        /// or a custom weapon-mount tag), so the weapon moves with the body animation.
        /// </summary>
        public static Vector3 GetAnimationOffset(PawnRenderNode node, PawnDrawParms parms)
        {
            Vector3 total = Vector3.zero;
            if (parms.Portrait) return total;
            for (PawnRenderNode n = node; n != null; n = n.parent)
            {
                if (n.TryGetAnimationOffset(parms, out Vector3 offset))
                {
                    total += offset;
                }
            }
            total.y = 0f;
            return total;
        }

        public static void DrawTuret(Pawn pawn, CompVehicleWeapon compWeapon, Thing equipment, Vector3 animationOffset = default)
        {
            float aimAngle = compWeapon.CurrentAngle;
            Vector3 drawLoc = pawn.DrawPos + compWeapon.GetOffsetByRot() + animationOffset;
            drawLoc.y += Altitudes.AltInc * compWeapon.Props.drawData.LayerForRot(pawn.Rotation, 1);
            float num = aimAngle - 90f;
            Mesh mesh;
            mesh = MeshPool.plane10;
            num %= 360f;
            Vector3 drawSize = compWeapon.Props.drawSize != 0 ? Vector3.one * compWeapon.Props.drawSize : (Vector3)equipment.Graphic.drawSize;
            Matrix4x4 matrix = Matrix4x4.TRS(drawLoc, Quaternion.AngleAxis(num, Vector3.up), new Vector3(drawSize.x, 1f, drawSize.y));
            var mat = (!(equipment.Graphic is Graphic_StackCount graphic_StackCount)) ?
                equipment.Graphic.MatSingle :
                graphic_StackCount.SubGraphicForStackCount(1, equipment.def).MatSingle;

            Graphics.DrawMesh(mesh, matrix, mat, 0);
        }
    }
    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAndApparelExtras))]
    internal static class Patch_DrawVehicleTurret
    {
        [HarmonyPriority(600)]
        public static bool Prefix(Pawn pawn, Vector3 drawPos, Rot4 facing, PawnRenderFlags flags)
        {
            CompVehicleWeapon compWeapon = pawn.TryGetComp<CompVehicleWeapon>();
            if (compWeapon != null)
            {
                return false;
            }
            return true;
        }
    }
}
