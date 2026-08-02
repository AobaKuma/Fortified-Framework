using Multiplayer.API;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;
using static UnityEngine.Networking.UnityWebRequest;

namespace Fortified
{
	public class CompMultipleTurretGun : ThingComp
	{
		public bool IsApparel => this.parent.def.IsApparel;
		public Pawn PawnOwner
		{
			get
			{
				if (parent is Pawn result) return result;
				if (parent is Apparel apparel) return apparel.Wearer;
				return null;
			}
		}
		public CompPropertiesMultipleTurretGun Props => (CompPropertiesMultipleTurretGun)this.props;
		public override void PostSpawnSetup(bool respawningAfterLoad)
		{
			base.PostSpawnSetup(respawningAfterLoad);
			if (respawningAfterLoad)
			{
				SetupTurrets();
			}
			turrets.RemoveDuplicates((a, b) => a.ID == b.ID);
			currentTurret ??= turrets.First().ID;
		}

		public override void PostPostMake()
		{
			base.PostPostMake();
			SetupTurrets();
		}
		private void SetupTurrets()
		{
			Props.subTurrets.ForEach(t =>
			{
				if (!turrets.Any(x => x.ID == t.ID))
				{
					SubTurret turret = new SubTurret() { ID = t.ID, parent = this.PawnOwner };
					turret.Init(t);
					turrets.Add(turret);
				}
			});
			//turrets.RemoveDuplicates((a, b) => a.ID == b.ID);
			currentTurret ??= turrets.First().ID;
		}
		public override void CompTick()
		{
			base.CompTick();
			if (!this.parent.Spawned) return;
			for (int i = 0; i < turrets.Count; i++)
			{
				turrets[i].Tick();
			}
		}

		public override void PostDrawExtraSelectionOverlays()
		{
			for (int i = 0; i < turrets.Count; i++)
			{
				if (turrets[i].targetForced && turrets[i].currentTarget.IsValid)
				{
					GenDraw.DrawLineBetween(parent.TrueCenter(), turrets[i].currentTarget.CenterVector3, Building_TurretGun.ForcedTargetLineMat);
				}
			}
		}
		public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
		{
			foreach (Gizmo item in base.CompGetWornGizmosExtra())
			{
				yield return item;
			}
			if (!IsApparel) yield break;
			foreach (Gizmo gizmo in GetGizmos())
			{
				yield return gizmo;
			}
		}

		public override IEnumerable<Gizmo> CompGetGizmosExtra()
		{
			foreach (Gizmo item in base.CompGetGizmosExtra())
			{
				yield return item;
			}
			if (IsApparel) yield break;
			foreach (Gizmo gizmo in GetGizmos())
			{
				yield return gizmo;
			}
		}
		private IEnumerable<Gizmo> GetGizmos()
		{
			if(PawnOwner?.Faction?.IsPlayer != true)
			{
				yield break;
			}
			List<SubTurret> selectedTurrets = new List<SubTurret>();
			if (Find.Selector.SelectedPawns.Count == 1)
			{
				selectedTurrets.AddRange(turrets);
				yield return new SubturretGizmo(this);
			}
			else
			{
				foreach (Pawn p in Find.Selector.SelectedPawns)
				{
					if (p.Faction?.IsPlayer == true)
					{
						if (p.TryGetComp(out CompMultipleTurretGun comp))
						{
							selectedTurrets.AddRange(comp.turrets);
						}
						if (p.apparel == null) continue;
						for (int i = 0; i < p.apparel.WornApparel.Count; i++)
						{
							if (p.apparel.WornApparel[i].TryGetComp(out CompMultipleTurretGun c))
							{
								selectedTurrets.AddRange(comp.turrets);
							}
						}
					}
				}
				if (selectedTurrets.NullOrEmpty())
				{
					yield break;
				}
				yield return new Command_Action
				{
					defaultLabel = "CommandSetForceAttackTarget".Translate(),
					defaultDesc = "CommandSetForceAttackTargetDesc".Translate(),
					icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack"),
					alsoClickIfOtherInGroupClicked = false,
					groupable = true,
					action = delegate
					{
						selectedTurrets[0].Targetting(selectedTurrets);
					}
				};
			}
			if(selectedTurrets.Any(x => x.Ammo != null))
			{
				yield return new Command_Action
				{
					defaultLabel = "FFF.MultiTurret.CommandSetAmmoSettings".Translate(),
					defaultDesc = "FFF.MultiTurret.CommandSetAmmoSettingsDesc".Translate(),
					icon = ContentFinder<Texture2D>.Get("UI/FFF_SelectAmmo"),
					alsoClickIfOtherInGroupClicked = false,
					groupable = true,
					action = delegate
					{
						Find.WindowStack.Add(new Dialog_SelectTurretsAmmo(selectedTurrets));
					}
				};
			}
		}

		public override void Notify_Equipped(Pawn pawn)
		{
			base.Notify_Equipped(pawn);
			SetupTurrets();
		}
		public override void Notify_Unequipped(Pawn pawn)
		{
			base.Notify_Unequipped(pawn);
		}
		public override void PostExposeData()
		{
			base.PostExposeData();
			Scribe_Collections.Look(ref turrets, "turrets", LookMode.Deep);
			Scribe_Values.Look(ref currentTurret, "currentTurrent");
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				Init();
			}
		}
		public override void Notify_DefsHotReloaded()
		{
			base.Notify_DefsHotReloaded();
			Init();
		}
		public override void CompDrawWornExtras()
		{
			base.CompDrawWornExtras();
			if (!IsApparel || PawnOwner == null || PawnOwner.DeadOrDowned) return;
			foreach (SubTurret t in turrets)
			{
				if (t.TurretProp.renderNodeProperty == null) continue;
				DrawTurret(PawnOwner, t, t.turret);
			}
		}

		protected void DrawTurret(Pawn pawn, SubTurret turret, Thing equipment)
		{
			var item = turret.TurretProp.renderNodeProperty;
			if (item.nodeClass.IsAssignableFrom(typeof(Fortified.PawnRenderNode_SubTurretGun)))
			{
				float aimAngle = (turret.HasTarget) ? turret.curRotation : item.drawData.RotationOffsetForRot(pawn.Rotation) + pawn.Rotation.AsAngle;
				aimAngle -= 90;
				aimAngle %= 360;
				Vector3 drawLoc = pawn.DrawPos + item.drawData.OffsetForRot(pawn.Rotation);
				Vector3 drawsize = new Vector3(item.drawSize.x, 0f, item.drawSize.y);

				Mesh mesh;
				if (aimAngle > 20f && aimAngle < 160f)
				{
					mesh = MeshPool.plane10;
					aimAngle += equipment.def.equippedAngleOffset;
				}
				else if (aimAngle > 200f && aimAngle < 340f)
				{
					mesh = MeshPool.plane10Flip;
					aimAngle -= 180f;
					aimAngle -= equipment.def.equippedAngleOffset;
				}
				else
				{
					mesh = MeshPool.plane10;
					aimAngle += equipment.def.equippedAngleOffset;
				}
				aimAngle %= 360f;
				drawLoc.y = Altitudes.AltInc * item.drawData.LayerForRot(pawn.Rotation, 1) + pawn.DrawPos.y;
				Material material = ((!(equipment.Graphic is Graphic_StackCount graphic_StackCount)) ? equipment.Graphic.MatSingleFor(equipment) : graphic_StackCount.SubGraphicForStackCount(1, equipment.def).MatSingleFor(equipment));
				Matrix4x4 matrix = Matrix4x4.TRS(s: drawsize, pos: drawLoc, q: Quaternion.AngleAxis(aimAngle, Vector3.up));
				Graphics.DrawMesh(mesh, matrix, material, 0);
			}
		}

		public override List<PawnRenderNode> CompRenderNodes()
		{
			if (parent is Pawn result)
			{
				List<PawnRenderNode> list = new List<PawnRenderNode>();
				foreach (SubTurret t in turrets)
				{
					if (t.TurretProp.renderNodeProperty == null || !t.HasTurret(result))
					{
						continue;
					}
					list.Add(t.RenderNode(result));
				}
				return list;
			}
			return base.CompRenderNodes();
		}

		public void PostGenInit(Pawn pawn)
		{
			if(pawn.ageTracker.AgeBiologicalTicks < 60L)
			{
				return;
			}
			foreach (SubTurret t in turrets)
			{
				t.parent = PawnOwner;
				if (t.TurretProp.generateWithWeapons.NullOrEmpty())
				{
					continue;
				}
				List<ThingStuffPair> workingWeapons = new List<ThingStuffPair>();
				for (int i = 0; i < PawnWeaponGenerator.AllWeaponPairs.Count; i++)
				{
					ThingStuffPair w = PawnWeaponGenerator.AllWeaponPairs[i];
					if (w.thing.IsRangedWeapon && w.thing.weaponTags?.Contains(t.TurretProp.supportedWeaponTag) == true && t.TurretProp.generateWithWeapons.Any((string tag) => w.thing.weaponTags.Contains(tag)) && (!(w.thing.generateAllowChance < 1f) || Rand.ChanceSeeded(w.thing.generateAllowChance, pawn.thingIDNumber ^ w.thing.shortHash ^ 0x1B3B648)))
					{
						workingWeapons.Add(w);
					}
				}
				if (workingWeapons.Count == 0)
				{
					return;
				}
				if (workingWeapons.TryRandomElementByWeight((ThingStuffPair pair) => pair.Commonality, out var result))
				{
					ThingWithComps thingWithComps = (ThingWithComps)ThingMaker.MakeThing(result.thing, result.stuff);
					PawnGenerator.PostProcessGeneratedGear(thingWithComps, pawn);
					CompEquippable compEquippable = thingWithComps.TryGetComp<CompEquippable>();
					if (compEquippable != null)
					{
						if (pawn.kindDef.weaponStyleDef != null)
						{
							compEquippable.parent.StyleDef = pawn.kindDef.weaponStyleDef;
						}
						else if (pawn.Ideo != null)
						{
							compEquippable.parent.StyleDef = pawn.Ideo.GetStyleFor(thingWithComps.def);
						}
					}
					t.AddWeapon(thingWithComps);
				}
			}
			Init();
			foreach (SubTurret t in turrets)
			{
				t.Ammo?.PostGenInit(pawn);
			}
		}

		public void Init()
		{
			foreach (var t in turrets)
			{
				t.parent = PawnOwner;
				t.Init(Props.subTurrets.Find(
					 p => p.ID == t.ID));
			}
		}

		public List<SubTurret> turrets = new List<SubTurret>();

		public string currentTurret;
	}

	public class CompPropertiesMultipleTurretGun : CompProperties
	{
		public CompPropertiesMultipleTurretGun()
		{
			this.compClass = typeof(CompMultipleTurretGun);
		}

		public List<SubTurretProperties> subTurrets;

		public override void ResolveReferences(ThingDef parentDef)
		{
			base.ResolveReferences(parentDef);
			foreach (SubTurretProperties t in subTurrets)
			{
				if (!t.renderNodeProperties.NullOrEmpty())
				{
					t.renderNodeProperty = t.renderNodeProperties[0];//compatibility stuff, should be removed in DMS2/1.7
				}
			}
		}

		public override IEnumerable<StatDrawEntry> SpecialDisplayStats(StatRequest req)
		{
			if (!subTurrets.NullOrEmpty())
			{
				List<ThingDef> defs = DefDatabase<ThingDef>.AllDefs.Where(x => x.IsRangedWeapon).ToList();
				CompMultipleTurretGun comp = req.Thing?.TryGetComp<CompMultipleTurretGun>();
				for (int i = 0; i < subTurrets.Count; i++)
				{
					SubTurretProperties t = subTurrets[i];
					string turretDesc = t.supportedWeaponTag.NullOrEmpty() ? t.turret.DescriptionDetailed : t.supportedWeaponTag.Translate();
					yield return new StatDrawEntry(FFF_DefOf.FFF_Turrets, "FFF.MultiTurret.WeaponSlot".Translate() + " " + (i + 1), comp?.turrets[i]?.turret?.LabelCap ?? t.turret?.LabelCap ?? turretDesc, turretDesc, 5600, null, GetHyperlinks(t), false, false);
				}
				IEnumerable<Dialog_InfoCard.Hyperlink> GetHyperlinks(SubTurretProperties props)
				{
					if (props.supportedWeaponTag.NullOrEmpty())
					{
						yield return new Dialog_InfoCard.Hyperlink(props.turret);
						yield break;
					}
					foreach (ThingDef def in defs.Where(y => y.weaponTags?.Contains(props.supportedWeaponTag) == true))
					{
						yield return new Dialog_InfoCard.Hyperlink(def);
					}
				}
			}
		}
	}
}
