using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using static HarmonyLib.Code;

namespace Fortified
{
	public class Building_RollingDoor_Access : Building_RollingDoor
	{
		private CompAccessKeyActivatable cachedComp;

		public CompAccessKeyActivatable Comp
		{
			get
			{
				if(cachedComp == null)
				{
					cachedComp = GetComp<CompAccessKeyActivatable>();
				}
				return cachedComp;
			}
		}

		private Graphic cachedPanelGraphic;

		public Graphic PanelGraphic
		{
			get
			{
				if (cachedPanelGraphic == null)
				{
					cachedPanelGraphic = GraphicDatabase.Get<Graphic_Single>(def.graphicData.texPath + "_Panel", ShaderDatabase.Cutout, Vector2.one, Color.white);
				}
				return cachedPanelGraphic;
			}
		}

		public override bool PawnCanOpen(Pawn p)
		{
			if (Comp.activated)
			{
				return base.PawnCanOpen(p);
			}
			return false;
		}

		protected override bool CheckFaction => false;

		public override AcceptanceReport ClaimableBy(Faction by)
		{
			if (!Comp.activated)
			{
				return false;
			}
			return base.ClaimableBy(by);
		}

		protected override void DrawAt(Vector3 drawLoc, bool flip = false)
		{
			base.DrawAt(drawLoc, flip);
			if (!Comp.activated)
			{
				drawLoc.y += Altitudes.AltInc;
				PanelGraphic.Draw(drawLoc, Rotation, this);
			}
		}

		protected override void ReceiveCompSignal(string signal)
		{
			base.ReceiveCompSignal(signal);
			if(signal == "FFF_ActivatedByAccessKey")
			{
				SetFaction(null);
			}
		}
	}

	public class Building_RollingDoor_AccessLink : Building_RollingDoor, IAccessKeyWanter
	{
		public int countToActivate = -1;

		public bool activated = false;

		public override bool PawnCanOpen(Pawn p)
		{
			if (activated)
			{
				return base.PawnCanOpen(p);
			}
			return false;
		}

		protected override bool AlwaysOpen => activated;

		protected override bool CheckFaction => false;

		public override AcceptanceReport ClaimableBy(Faction by)
		{
			if (!activated)
			{
				return false;
			}
			return base.ClaimableBy(by);
		}

		public void Notify_AccessKeyUsed(CompAccessKeyActivatable comp, Pawn pawn = null)
		{
			if (activated) return;
			countToActivate--;
			if (countToActivate <= 0)
			{
				activated = true;
				SetFaction(pawn?.Faction);
				DoorOpen();
			}
		}

		public void Notify_LinkedTo(CompAccessKeyActivatable comp)
		{
			if (activated) return;
			if(countToActivate < 0)
			{
				countToActivate = 0;
			}
			countToActivate++;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref activated, "activated", defaultValue: false);
			Scribe_Values.Look(ref countToActivate, "countToActivate", defaultValue: -1);
		}
	}
}
