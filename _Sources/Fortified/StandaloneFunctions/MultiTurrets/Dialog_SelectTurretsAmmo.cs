using LudeonTK;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static Mono.Security.X509.X520;
using static UnityEngine.GraphicsBuffer;

namespace Fortified
{
	public class Dialog_SelectTurretsAmmo : Window
	{
		private List<SubTurret> subTurrets = new List<SubTurret>();

		private Vector2 scrollPosition;

		private float viewHeight;

		public override Vector2 InitialSize => new Vector2(550, 700);

		public Dialog_SelectTurretsAmmo(List<SubTurret> subTurrets)
		{
			this.subTurrets = subTurrets;
			this.subTurrets.RemoveWhere(x => x.Ammo == null);
			this.doCloseX = true;
			this.doCloseButton = false;
			this.forcePause = true;
			this.closeOnClickedOutside = true;
			this.absorbInputAroundWindow = true;
		}

		public override void DoWindowContents(Rect inRect)
		{
			Text.Font = GameFont.Small;
			Text.Anchor = TextAnchor.MiddleLeft;
			Rect rect = inRect.ContractedBy(10);
			Rect viewRect = new Rect(0f, 0f, rect.width - 16f, viewHeight);
			Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
			Widgets.BeginGroup(viewRect);
			float curY = 0f;
			foreach(SubTurret item in subTurrets)
			{
				DrawSubTurret(item, viewRect.width, ref curY);
			}
			viewHeight = curY;
			Widgets.EndGroup();
			Widgets.EndScrollView();
			Text.Anchor = TextAnchor.UpperLeft;
		}

		public void DrawSubTurret(SubTurret turret, float width, ref float curY)
		{
			List<ThingDef> list = turret.Ammo.Props.AllAcceptedAmmo().ToList();
			Rect rect = new Rect(0, curY, width, 50f + (list.Count * 36f));
			Widgets.DrawAltRect(rect);
			Widgets.LabelWithIcon(new Rect(0, curY, width, 48f), turret.turret.LabelCap + " (" + turret.PawnOwner.LabelCap + ")", turret.turret.def.uiIcon);
			curY += 50f;
			CompTurretProjectile comp = turret.Ammo;
			foreach(ThingDef def in list)
			{
				int count = comp.ammoSettings.GetValueOrDefault(def);
				int newCount = DrawThingRow(def, count, width, ref curY);
				if(newCount != count)
				{
					if(newCount == 0)
					{
						comp.ammoSettings.Remove(def);
					}
					else
					{
						comp.ammoSettings.SetOrAdd(def, newCount);
					}
				}
			}
		}

		public int DrawThingRow(ThingDef def, int count, float width, ref float curY)
		{
			Rect rect1 = new Rect(10, curY, (width - 10) / 2f, 36f);
			Rect rect2 = new Rect(10 + rect1.width, curY, rect1.width, 36f);
			Widgets.LabelWithIcon(rect1, def.LabelCap + ": " + count, def.uiIcon);
			curY += 36f;
			return Mathf.RoundToInt(Widgets.HorizontalSlider(rect2, count, 0, def.stackLimit, leftAlignedLabel: "0", rightAlignedLabel: def.stackLimit.ToString()));
		}
	}
}
