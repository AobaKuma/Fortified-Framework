using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{
    public class PawnRenderNode_SubTurretGun : PawnRenderNode
    {
        public SubTurret subturret;
        public PawnRenderNode_SubTurretGun(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree) : base(pawn, props, tree)
		{
		}
		public override Graphic GraphicFor(Pawn pawn)
		{
			return GraphicDatabase.Get<Graphic_Single>(subturret.turret.def.graphicData.texPath, ShaderDatabase.Cutout); 
		}
	}
}
