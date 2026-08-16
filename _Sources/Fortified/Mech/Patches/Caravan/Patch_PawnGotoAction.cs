using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Fortified
{
	[HarmonyPatch(typeof(FloatMenuOptionProvider_DraftedMove), nameof(FloatMenuOptionProvider_DraftedMove.PawnGotoAction))]
	public static class Patch_PawnGotoAction
	{
		[HarmonyPrefix]
		public static bool Prefix(IntVec3 clickCell, Pawn pawn, IntVec3 gotoLoc)
		{
			if (pawn is ICaravanOwner owner && owner.CanCaravan)
			{
				bool flag;
				if (pawn.Position == gotoLoc || (pawn.CurJobDef == JobDefOf.Goto && pawn.CurJob.targetA.Cell == gotoLoc))
				{
					flag = true;
				}
				else
				{
					Job job = JobMaker.MakeJob(JobDefOf.Goto, gotoLoc);
					if (pawn.Map.exitMapGrid.IsExitCell(clickCell))
					{
						job.exitMapOnArrival = true;
					}
					else if (!pawn.Map.IsPlayerHome && !pawn.Map.exitMapGrid.MapUsesExitGrid && CellRect.WholeMap(pawn.Map).IsOnEdge(clickCell, 3) && pawn.Map.Parent.GetComponent<FormCaravanComp>() != null && MessagesRepeatAvoider.MessageShowAllowed("MessagePlayerTriedToLeaveMapViaExitGrid-" + pawn.Map.uniqueID, 60f))
					{
						if (pawn.Map.Parent.GetComponent<FormCaravanComp>().CanFormOrReformCaravanNow)
						{
							Messages.Message("MessagePlayerTriedToLeaveMapViaExitGrid_CanReform".Translate(), pawn.Map.Parent, MessageTypeDefOf.RejectInput, historical: false);
						}
						else
						{
							Messages.Message("MessagePlayerTriedToLeaveMapViaExitGrid_CantReform".Translate(), pawn.Map.Parent, MessageTypeDefOf.RejectInput, historical: false);
						}
					}
					flag = pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
				}
				if (flag)
				{
					FleckMaker.Static(gotoLoc, pawn.Map, FleckDefOf.FeedbackGoto);
				}
				return false;
			}
			return true;
		}
	}
}
