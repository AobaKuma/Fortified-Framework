using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.Sound;
using static HarmonyLib.Code;
using static Mono.Math.BigInteger;

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
		private Texture2D cachedUIIcon;
		public Texture2D UIIcon
		{
			get
			{
				if (cachedUIIcon == null)
				{
					cachedUIIcon = ContentFinder<Texture2D>.Get("UI/FFF_SelectOverseerSubject");
				}
				return cachedUIIcon;
			}
		}
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

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			if (Comp.activeInt)
			{
				if (!Power.PowerOn)
				{
					Comp.activeInt = false;
					Comp.Notify_BandwidthChanged();
				}
			}
			else if (Power.PowerOn)
			{
				Comp.activeInt = true;
				Comp.Notify_BandwidthChanged();
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

		protected override void ReceiveCompSignal(string signal)
		{
			base.ReceiveCompSignal(signal);
			if(Comp.activeInt)
			{
				if(signal == "PowerTurnedOff")
				{
					Comp.activeInt = false;
					Comp.Notify_BandwidthChanged();
				}
			}
			else if (signal == "PowerTurnedOn")
			{
				Comp.activeInt = true;
				Comp.Notify_BandwidthChanged();
			}
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
