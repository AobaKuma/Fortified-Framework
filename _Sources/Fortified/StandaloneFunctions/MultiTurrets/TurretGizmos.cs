using Multiplayer.API;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Noise;
using static HarmonyLib.Code;
using static UnityEngine.GraphicsBuffer;

namespace Fortified
{
	[StaticConstructorOnStartup]
	public class SubturretGizmo : Gizmo
	{
		public SubturretGizmo(CompMultipleTurretGun comp)
		{
			this.comp = comp;
			this.subTurrets = comp.turrets;
			this.subTurret = comp.turrets.Find(t => t.ID == comp.currentTurret);
			this.Order = -80f;
		}

		CompMultipleTurretGun comp;
		private List<SubTurret> subTurrets;
		private SubTurret subTurret;
		private static readonly CachedTexture ToggleTurretIcon = new CachedTexture("UI/Gizmos/ToggleTurret");
		private static readonly CachedTexture SelectWeaponIcon = new CachedTexture("UI/Commands/Halt");
		private static readonly CachedTexture DropWeaponIcon = new CachedTexture("UI/Buttons/Drop");
		private static readonly CachedTexture UseAnyAmmoIcon = new CachedTexture("UI/FFF_SelectAmmo");
		private bool drawRadius = true;

		public override bool Visible
		{
			get
			{
				return true;
			}
		}
		public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
		{
			Pawn owner = subTurret.PawnOwner;
			bool multiselect = KeyBindingDefOf.QueueOrder.IsDown;
			string multiselectKeyLabel = KeyBindingDefOf.QueueOrder.MainKeyLabel;
			bool rightClick = Event.current.button == 1;
			Rect outline = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
			Rect inner = outline.ContractedBy(6f);
			GUI.color = (parms.lowLight ? Command.LowLightBgColor : Color.white);
			GenUI.DrawTextureWithMaterial(outline, Command.BGTex, null, default(Rect));
			GUI.color = Color.white;
			Text.Anchor = TextAnchor.UpperLeft;
			Text.Font = GameFont.Small;
			bool onGizmo = false;
			if (Mouse.IsOver(outline)) onGizmo = true;

			TaggedString taggedString = new TaggedString();
			//add text here
			taggedString += subTurret.turret?.LabelCap ?? ("FFF.MultiTurret.WeaponSlot".Translate() + " " + (comp.turrets.IndexOf(subTurret) + 1) + "(" + (subTurret.TurretProp.supportedWeaponTag).Translate() + ")");
			taggedString = taggedString.Truncate(inner.width, null);
			Vector2 vector = Text.CalcSize(taggedString);
			Rect turretNameRect = inner;
			turretNameRect.width = inner.width;
			turretNameRect.height = vector.y;

			Widgets.Label(turretNameRect, taggedString);
			if (Mouse.IsOver(turretNameRect))
			{
				Widgets.DrawHighlight(turretNameRect);
			}
			if (Widgets.ButtonInvisible(turretNameRect, false))
			{
				Find.WindowStack.Add(new FloatMenu(GetTurretOptions().ToList<FloatMenuOption>()));
			}

			if (!subTurret.HasTurret(owner))
			{
				Rect selectWeaponRect = new Rect(inner.x, inner.y + vector.y + 5f, inner.width, inner.height - (vector.y + 5f));
				if (Mouse.IsOver(selectWeaponRect))
				{
					Widgets.DrawHighlight(selectWeaponRect);
				}
				Text.Anchor = TextAnchor.MiddleCenter;
				Widgets.Label(selectWeaponRect, "FFF.SelectWeapon".Translate());
				Text.Anchor = TextAnchor.UpperLeft;
				if (Widgets.ButtonInvisible(selectWeaponRect, false))
				{
					TargetNewWeapon(subTurret);
				}
				drawRadius = false;
				return new GizmoResult(onGizmo ? GizmoState.Mouseover : GizmoState.Clear);
			}

			//好丑，不会设计UI呜呜呜 //0v0

			List<SubTurret> list = comp.turrets.Where(x => x.HasTurret(owner)).ToList();
			multiselect = multiselect && list.Count > 1;
			float weaponSize = inner.height - turretNameRect.height;
			Rect weaponRect = new Rect(inner.x, inner.y + turretNameRect.height, 40f, 40f);

			if (multiselect)
			{
				Rect subInfoRect = new Rect(weaponRect);

				float height = subInfoRect.height / list.Count;
				subInfoRect.height = height;
				for (int i = 0; i < list.Count; i++)
				{
					DrawCooldownBar(subInfoRect, list[i]);
					Widgets.DrawTextureFitted(new Rect(subInfoRect.x, subInfoRect.y, subInfoRect.height, subInfoRect.height), list[i].turret.def.uiIcon, 1f);
					subInfoRect.y += height;
				}
			}
			else
			{
				DrawCooldownBar(weaponRect, subTurret);
				Widgets.DrawTextureFitted(weaponRect, subTurret.turret.def.uiIcon, 1f);
			}


			if (Mouse.IsOver(weaponRect))
			{
				drawRadius = !multiselect;
				Widgets.DrawHighlight(weaponRect);
				TooltipHandler.TipRegion(weaponRect, "FFF.MultiTurret.AttackRectTip".Translate(multiselectKeyLabel));
			}
			else drawRadius = false;

			if (Widgets.ButtonInvisible(weaponRect, false))
			{
				if (rightClick)
				{
					if (!multiselect)
					{
						subTurret.ClearTarget();
					}
					else
					{
						for (int j = 0; j < list.Count; j++)
						{
							list[j].ClearTarget();
						}
					}

				}
				else subTurret.Targetting(multiselect ? list : null);
			}
			//
			Rect infoRect = new Rect(weaponRect.x + weaponRect.width + 5f, weaponRect.y, inner.width - (weaponRect.width + 5f), weaponRect.height / 2f - 10f);
			/*DrawCooldownBar(infoRect, subTurret);*/

			#region Buttons

			bool drawWeaponInteractRect = multiselect ? subTurrets.Any(x => !x.TurretProp.supportedWeaponTag.NullOrEmpty()) : !subTurret.TurretProp.supportedWeaponTag.NullOrEmpty();
			bool drawAmmoRect = !multiselect && subTurret.Ammo != null;
			int buttonCount = 1;
			if (drawWeaponInteractRect)
			{
				buttonCount++;
			}
			if (drawAmmoRect)
			{
				buttonCount++;
			}
			float buttonWidth = Mathf.Min((inner.width - (40f + (5f * buttonCount))) / (float)buttonCount, 40f);
			float buttonY = weaponRect.y + ((40f - buttonWidth) / 2f);

			Rect buttonRect1 = new Rect(weaponRect.xMax + 5f, buttonY, buttonWidth, buttonWidth);

			#region AutoAttack

			if (Mouse.IsOver(buttonRect1))
			{
				Widgets.DrawHighlight(buttonRect1);
				TooltipHandler.TipRegion(buttonRect1, "FFF.MultiTurret.AutoAttackTip".Translate(multiselectKeyLabel));
			}
			Widgets.DrawTextureFitted(buttonRect1, ToggleTurretIcon.Texture, 1f);
			bool autofire = Widgets.ButtonInvisible(buttonRect1);

			Rect rect = new Rect(buttonRect1.x + (buttonRect1.width * 0.5f), buttonRect1.y, (buttonRect1.width * 0.5f), (buttonRect1.width * 0.5f));
			Texture2D image;
			image = subTurret.fireAtWill ? Widgets.CheckboxOnTex : Widgets.CheckboxOffTex;
			GUI.DrawTexture(rect, image, ScaleMode.ScaleToFit);
			if (autofire)
			{
				bool flag = !subTurret.fireAtWill;
				[SyncMethod] void SyncAutoFire(SubTurret subTurret) { subTurret.SwitchAutoFire(flag); } //autofire sync not working yet
				if (multiselect)
				{
					for (int i = 0; i < comp.turrets.Count; i++)
					{
						SyncAutoFire(comp.turrets[i]);
					}
				}
				else SyncAutoFire(subTurret);
			}

			#endregion

			Rect buttonRect2 = new Rect(buttonRect1);
			buttonRect2.x += buttonWidth + 5f;

			if (drawWeaponInteractRect)
			{
				Widgets.DrawTextureFitted(buttonRect2, DropWeaponIcon.Texture, 1f);
				if (Mouse.IsOver(buttonRect2))
				{
					Widgets.DrawHighlight(buttonRect2);
					TooltipHandler.TipRegion(buttonRect2, "FFF.MultiTurret.DropWeaponTip".Translate(multiselectKeyLabel));
				}
				if (Widgets.ButtonInvisible(buttonRect2, false))
				{
					if (multiselect)
					{
						for (int i = 0; i < comp.turrets.Count; i++)
						{
							if (comp.turrets[i].HasTurret(owner) && !comp.turrets[i].TurretProp.supportedWeaponTag.NullOrEmpty())
							{
								comp.turrets[i].RemoveWeapon();
							}
						}
					}
					else if (subTurret.turret != null)
					{
						subTurret.RemoveWeapon();
					}
					return new GizmoResult(onGizmo ? GizmoState.Mouseover : GizmoState.Clear); //To prevent possible issues
				}
			}

			if (drawAmmoRect)
			{
				Rect buttonRect3 = new Rect(buttonRect2);
				if (drawWeaponInteractRect)
				{
					buttonRect3.x += buttonWidth + 5f;
				}
				Texture2D ammoIcon = subTurret.Ammo.selectedAmmoDef == null ? UseAnyAmmoIcon.Texture : subTurret.Ammo.selectedAmmoDef.uiIcon;
				Widgets.DrawTextureFitted(buttonRect3, ammoIcon, 1f);
				if (Mouse.IsOver(buttonRect3))
				{
					Widgets.DrawHighlight(buttonRect3);
					TooltipHandler.TipRegion(buttonRect3, "FFF.MultiTurret.SelectAmmoTypeTip".Translate(subTurret.Ammo.selectedAmmoDef == null ? "AnyLower".Translate() : subTurret.Ammo.selectedAmmoDef.label));
				}
				if (Widgets.ButtonInvisible(buttonRect3, false))
				{
					List<FloatMenuOption> options = new List<FloatMenuOption>();
					options.Add(new FloatMenuOption("AnyLower".Translate().CapitalizeFirst(), delegate
					{
						subTurret.Ammo.selectedAmmoDef = null;
						subTurret.cannotShootNoAmmo = false;
					}, UseAnyAmmoIcon.Texture, Color.white));
					foreach(ThingDef ammo in subTurret.Ammo.Props.AllAcceptedAmmo())
					{
						ThingDef ammoLocal = ammo;
						options.Add(new FloatMenuOption(ammoLocal.LabelCap, delegate
						{
							subTurret.Ammo.selectedAmmoDef = ammoLocal;
							subTurret.cannotShootNoAmmo = false;
						}, ammoLocal));
					}
					Find.WindowStack.Add(new FloatMenu(options));
				}
			}

			#endregion

			return new GizmoResult(onGizmo ? GizmoState.Mouseover : GizmoState.Clear);
		}

		public void TargetNewWeapon(SubTurret turret)
		{
			Find.Targeter.BeginTargeting(TargetingParameters.ForThing(), delegate (LocalTargetInfo t)
			{
				if (t.Thing is ThingWithComps thing)
				{
					Job job = JobMaker.MakeJob(FFF_DefOf.FFF_EquipTurret, thing);
					job.count = comp.turrets.IndexOf(turret) + 1;
					turret.PawnOwner.jobs.TryTakeOrderedJob(job, JobTag.Misc);
				}
			}, delegate (LocalTargetInfo t)
			{
				if (t.IsValid)
				{
					if (ValidateTarget(t))
					{
						GenDraw.DrawTargetHighlight(t);
					}
				}

			}, ValidateTarget, null, null, SelectWeaponIcon.Texture, playSoundOnAction: true, null);
			bool ValidateTarget(LocalTargetInfo t)
			{
				if (t.Thing is ThingWithComps thing && thing.def.IsRangedWeapon && thing.def.weaponTags?.Contains(turret.TurretProp.supportedWeaponTag) == true)
				{
					return true;
				}
				return false;
			}
		}

		public override void GizmoUpdateOnMouseover()
		{
			if (!this.drawRadius)
			{
				return;
			}
			subTurret.CurrentEffectiveVerb.verbProps.DrawRadiusRing(subTurret.CurrentEffectiveVerb.caster.Position);
		}

		private static readonly Texture2D cooldownBarTex = SolidColorMaterials.NewSolidColorTexture(new Color32(9, 203, 4, 64));

		private void DrawCooldownBar(Rect rect, SubTurret turret)
		{
			int cooldownTicksLeft = Mathf.Max(turret.burstCooldownTicksLeft + turret.burstWarmupTicksLeft, 0);
			/*if((cooldownTicksLeft <= 0 && turret.CurrentEffectiveVerb.state != VerbState.Bursting) || !turret.HasTarget)
			{
				cooldownTicksLeft = turret.TurretProp.warmingTime.SecondsToTicks();
			}*/
			if (cooldownTicksLeft > 0)
			{
				float value = Mathf.InverseLerp(0f, turret.CooldownTimeAdjusted + turret.WarmupTimeAdjusted, cooldownTicksLeft);
				Widgets.FillableBar(rect, Mathf.Clamp01(value), cooldownBarTex, null, doBorder: false);
			}
			/*GameFont font = Text.Font;
			Text.Font = GameFont.Tiny;
			string text = cooldownTicksLeft.ToStringTicksToPeriod();
			Vector2 textSize = Text.CalcSize(text);
			textSize.x += 2f;
			Rect rect2 = new Rect(rect);
			rect2.x = rect.x + rect.width / 2f - textSize.x / 2f;
			rect2.width = textSize.x;
			rect2.height = textSize.y;
			Rect position = rect2.ExpandedBy(8f, 0f);
			Text.Anchor = TextAnchor.UpperCenter;
			GUI.DrawTexture(position, TexUI.GrayTextBG);
			Widgets.Label(rect2, text);
			Text.Anchor = TextAnchor.UpperLeft;
			Text.Font = font;*/
		}

		public override float GetWidth(float maxWidth)
		{
			return 140f;
		}

		private IEnumerable<FloatMenuOption> GetTurretOptions()
		{
			if (subTurrets == null) yield break;
			foreach (var turret in this.subTurrets)
			{
				string text = "FFF.MultiTurret.WeaponSlot".Translate() + " " + (subTurrets.IndexOf(turret) + 1) + (turret.TurretProp.supportedWeaponTag.NullOrEmpty() ? "" : ("(" + (turret.TurretProp.supportedWeaponTag).Translate() + ")")) + (turret.turret == null ? "" : (": " + turret.turret.LabelCap));
				yield return new FloatMenuOption(text, delegate () //Float menu for switching between turrets
				{
					[SyncMethod] void SyncCurrentTurret(CompMultipleTurretGun comp, string turretId) { comp.currentTurret = turretId; }
					SyncCurrentTurret(comp, turret.ID);
				}, extraPartWidth: 29f, extraPartOnGUI: turret.turret == null ? null : (Rect r) => Widgets.InfoCardButton(r.x + 5f, r.y + (r.height - 24f) / 2f, turret.turret.def));
			}
			yield break;
		}
	}
}
