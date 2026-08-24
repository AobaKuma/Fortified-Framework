using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace Fortified
{
    public class PawnRenderNode_Head : PawnRenderNode
    {
        public PawnRenderNode_Head(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree) : base(pawn, props, tree)
        {
        }
        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            return props.overrideMeshSize != null
                ? MeshPool.GetMeshSetForSize(props.overrideMeshSize.Value)
                : HumanlikeMeshPoolUtility.GetHumanlikeHeadSetForPawn(pawn, 1f, 1f);
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            if (pawn is IHumanlikeMech mech)
            {
                return mech.HeadGraphic;
            }
            return pawn.story?.headType?.GetGraphic(pawn, ColorFor(pawn));
        }
    }
    public class PawnRenderNodeWorker_Head : PawnRenderNodeWorker_FlipWhenCrawling
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            return true;
        }
        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            Vector3 offset = parms.pawn.def.GetModExtension<HumanlikeMechExtension>().headOffset;
            Vector3 vector = base.OffsetFor(node, parms, out pivot) + parms.pawn.Drawer.renderer.BaseHeadOffsetAt(parms.facing)+ offset;
            if (node.Props.narrowCrownHorizontalOffset != 0f && parms.facing.IsHorizontal)
            {
                if (parms.facing == Rot4.East)
                {
                    vector.x -= node.Props.narrowCrownHorizontalOffset;
                }
                else if (parms.facing == Rot4.West)
                {
                    vector.x += node.Props.narrowCrownHorizontalOffset;
                }
                vector.z -= node.Props.narrowCrownHorizontalOffset;
            }
            return vector;
        }

        public override Quaternion RotationFor(PawnRenderNode node, PawnDrawParms parms)
        {
            Quaternion quaternion = base.RotationFor(node, parms);
            if (!parms.Portrait && parms.pawn.Crawling)
            {
                quaternion *= PawnRenderUtility.CrawlingHeadAngle(parms.facing).ToQuat();
                if (parms.flipHead)
                {
                    quaternion *= 180f.ToQuat();
                }
            }
            return quaternion;
        }
    }
}
