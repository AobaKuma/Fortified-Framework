using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Fortified
{
	public class Building_Overseer : Building, IOverseer, ITargetingSource
	{
		#region Targetable
		public bool CasterIsPawn => true;
		public bool IsMeleeAttack => false;
		public bool Targetable => true;
		public bool MultiSelect => false;
		public bool HidePawnTooltips => false;
		public Thing Caster => this;
		public Pawn CasterPawn => null;
		public Verb GetVerb => null;
		public TargetingParameters targetParams => new TargetingParameters()
		{
			canTargetPawns = true,
			canTargetLocations = false
		};
		public Texture2D UIIcon => TexCommand.Install;
		public ITargetingSource DestinationSelector => null;
		public bool CanHitTarget(LocalTargetInfo target)
		{
			return ValidateTarget(target, showMessages: false);
		}
		public bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
		{
			if (target.IsValid && target.HasThing && target.Thing is Pawn pawn)
			{
				AcceptanceReport acceptanceReport = MechanitorUtility.CanControlMech(Comp.dummyPawn, pawn);
				if (!acceptanceReport.Accepted)
				{
					if (showMessages && !acceptanceReport.Reason.NullOrEmpty())
					{
						Messages.Message(acceptanceReport.Reason.CapitalizeFirst(), pawn, MessageTypeDefOf.RejectInput, historical: false);
					}
					return false;
				}
				return true;
			}
			return false;
		}

		public void DrawHighlight(LocalTargetInfo target)
		{
			if (target.IsValid)
			{
				GenDraw.DrawTargetHighlight(target);
			}
		}

		public virtual void OrderForceTarget(LocalTargetInfo target)
		{
			if (target.IsValid && target.HasThing && target.Thing is Pawn pawn)
			{
				if (MechanitorUtility.CanControlMech(Comp.dummyPawn, pawn))
				{
					Comp.Connect(pawn);
				}
			}
		}

		public void OnGUI(LocalTargetInfo target)
		{
			if (ValidateTarget(target, showMessages: false))
			{
				GenUI.DrawMouseAttachment(UIIcon);
			}
			else
			{
				GenUI.DrawMouseAttachment(TexCommand.CannotShoot);
			}
		}
		#endregion

		private CompOverseer comp;

		public CompOverseer Comp
		{
			get
			{
				if (comp == null)
				{
					comp = GetComp<CompOverseer>();
				}
				return comp;
			}
		}

		private CompPowerTrader power;

		public CompPowerTrader Power
		{
			get
			{
				if (power == null)
				{
					power = GetComp<CompPowerTrader>();
				}
				return power;
			}
		}

		public override void SetFaction(Faction newFaction, Pawn recruiter = null)
		{
			base.SetFaction(newFaction, recruiter);
			if (newFaction != null && newFaction.IsPlayer)
			{
				Comp?.UpdateDummy();
			}
		}

		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			if (mode != DestroyMode.WillReplace)
			{
				Comp.dummyPawn?.mechanitor?.UndraftAllMechs();
			}
			base.DeSpawn(mode);
		}

		public override IEnumerable<Gizmo> GetGizmos()
		{
			if (Comp.MechanitorActive)
			{
				bool flag = !(Power?.PowerOn ?? true);
				foreach (Gizmo g in Comp.dummyPawn.mechanitor.GetGizmos())
				{
					if (g is Command_CallBossgroup)
					{
						continue;
					}
					if(g is MechanitorBandwidthGizmo)
					{
						yield return new OverseerBuildingBandwidthGizmo(Comp.dummyPawn.mechanitor);
						continue;
					}
					if (flag)
					{
						g.Disable("NoPower".Translate().CapitalizeFirst());
					}
					yield return g;
				}
				if (Spawned)
				{
					string defaultLabel = "FFF_SelectMechToControlLabel".Translate();
					string defaultDesc = "FFF_SelectMechToControlDesc".Translate();
					Command_Action command_Action = new Command_Action
					{
						defaultLabel = defaultLabel,
						defaultDesc = defaultDesc,
						icon = UIIcon,
						groupable = false,
						Order = -86f,
						action = delegate
						{
							SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
							Find.Targeter.BeginTargeting(this);
						}
					};
					if (flag)
					{
						command_Action.Disable("NoPower".Translate().CapitalizeFirst());
					}
					yield return command_Action;
				}
			}
			foreach (Gizmo g in base.GetGizmos())
			{
				yield return g;
			}
		}
	}
}
