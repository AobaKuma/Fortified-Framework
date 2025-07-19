
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Fortified;

//require PawnRenderNode_VehicleTurret
public class PawnRenderNodeWorker_VehicleTurret : PawnRenderNodeWorker
{
    // public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
    // {
    //     return base.CanDrawNow(node, parms);
    // }

    public override void AppendDrawRequests(PawnRenderNode node, PawnDrawParms parms, List<PawnGraphicDrawRequest> requests)
    {
        requests.Add(new PawnGraphicDrawRequest(node));
    }


    public override void PostDraw(PawnRenderNode node, PawnDrawParms parms, Mesh _, Matrix4x4 __)
    {
        if (node is PawnRenderNode_VehicleTurret nodeVehicleTurret && nodeVehicleTurret.TryGetCompVehicleWeapon(out CompVehicleWeapon compVehicleWeapon))
        {
            Pawn pawn = parms.pawn;
            float aimAngle = compVehicleWeapon.CurrentAngle;
            Vector3 drawLoc = pawn.DrawPos + compVehicleWeapon.GetOffsetByRot();
            drawLoc.y += Altitudes.AltInc * compVehicleWeapon.Props.drawData.LayerForRot(pawn.Rotation, 1);
            float num = aimAngle - 90f;
            Mesh mesh;
            mesh = MeshPool.plane10;
            num %= 360f;

            Thing equipment = pawn.equipment.Primary;

            Vector3 drawSize = compVehicleWeapon.Props.drawSize != 0 ? Vector3.one * compVehicleWeapon.Props.drawSize : (Vector3)equipment.Graphic.drawSize;
            Matrix4x4 matrix = Matrix4x4.TRS(drawLoc, Quaternion.AngleAxis(num, Vector3.up), new Vector3(drawSize.x, 1f, drawSize.y));
            var mat = equipment.Graphic.MatSingle;

            Graphics.DrawMesh(mesh, matrix, mat, 0);
        }
    }

}