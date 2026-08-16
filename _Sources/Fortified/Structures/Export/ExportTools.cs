using LudeonTK;
using Multiplayer.API;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Noise;
using static System.Collections.Specialized.BitVector32;

namespace Fortified.Structures
{
	public abstract class ExportTool : Thing
	{
		public abstract void ExportToXML(IntVec3 origin, StringBuilder sb);
	}

	public class Dialog_PawnGroupExtractTool : Window
	{
		public static ExportTool_PawnGroup buffer;

		private Vector2 scrollPosition = Vector2.zero;

		public override Vector2 InitialSize => new Vector2(400f, 500f);

		public ExportTool_PawnGroup tool;

		private List<PawnKindDef> kindDefs = new List<PawnKindDef>();

		public Dialog_PawnGroupExtractTool(ExportTool_PawnGroup tool)
		{
			this.resizeable = true;
			this.doCloseButton = false;
			this.doCloseX = true;
			forcePause = false;
			absorbInputAroundWindow = false;
			onlyOneOfTypeAllowed = false;
			this.draggable = true;
			this.tool = tool;
			this.closeOnClickedOutside = false;
			this.preventCameraMotion = false;
		}

		public override void DoWindowContents(Rect inRect)
		{
			inRect = inRect.ContractedBy(10);
			Text.Font = GameFont.Small;
			float curY = inRect.y;
			if (Widgets.ButtonText(new Rect(inRect.x, curY, inRect.width, 30f), tool.elementTypeName.NullOrEmpty() ? "None" : tool.elementTypeName))
			{
				List<FloatMenuOption> list = new List<FloatMenuOption>();
				list.Add(new FloatMenuOption(typeof(FFF_Element_PawnGroup).FullName, delegate
				{
					tool.elementTypeName = typeof(FFF_Element_PawnGroup).FullName;
				}));
				foreach (Type item in typeof(FFF_Element_PawnGroup).AllSubclassesNonAbstract())
				{
					Type localType = item;
					list.Add(new FloatMenuOption(localType.FullName, delegate
					{
						tool.elementTypeName = localType.FullName;
					}));
				}
				Find.WindowStack.Add(new FloatMenu(list));
			}
			curY += 30f;
			if (Widgets.ButtonText(new Rect(inRect.x, curY, inRect.width, 30f), tool.factionDef?.defName ?? "Null"))
			{
				List<FloatMenuOption> list = new List<FloatMenuOption>();
				foreach (FactionDef def in DefDatabase<FactionDef>.AllDefs)
				{
					FactionDef localDef = def;
					list.Add(new FloatMenuOption(localDef.defName, delegate
					{
						tool.factionDef = localDef;
						RegeneratePawnKinds(localDef);
					}));
				}
				Find.WindowStack.Add(new FloatMenu(list));
			}
			curY += 30f;
			Widgets.CheckboxLabeled(new Rect(inRect.x, curY, inRect.width, 30f), "Fixed pawns count", ref tool.fixedOptions);
			curY += 30f;
			if (!tool.fixedOptions)
			{
				Widgets.FloatRange(new Rect(inRect.x, curY, inRect.width, 30f), GetHashCode(), ref tool.pointsRange, 0f, 10000f, "Points range " + tool.pointsRange.ToString(), ToStringStyle.Integer, 1f, roundTo: 100f);
				curY += 30f;
			}
			tool.lordTag = Widgets.TextField(new Rect(inRect.x, curY, inRect.width - 60f, 30f), tool.lordTag, 20);
			if(Widgets.ButtonImage(new Rect(inRect.width - 60f, curY, 30f, 30f), TexButton.Copy))
			{
				buffer = tool;
			}
			if (Widgets.ButtonImage(new Rect(inRect.width - 30f, curY, 30f, 30f), TexButton.Paste))
			{
				if(buffer != null && buffer != tool)
				{
					tool.CopyFrom(buffer);
				}
			}
			curY += 30f;
			tool.sendSignalRadius = Widgets.HorizontalSlider(new Rect(inRect.x, curY, inRect.width, 30f), tool.sendSignalRadius, -1f, 80, roundTo: 1, label: $"Signal radius ({tool.sendSignalRadius})");
			curY += 35f;
			Widgets.DrawLineHorizontal(inRect.x, curY, inRect.width);
			curY += 5f;
			Rect outRect = new Rect(inRect.x, curY, inRect.width, inRect.height - curY);
			float width = outRect.width - 16f;
			Rect viewRect = new Rect(0f, 0f, width, (tool.options.Count + 2) * 30f);
			Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
			float num = 0f;
			foreach (var item in tool.options.ToList())
			{
				PawnGenOption localItem = item;
				if (Widgets.ButtonText(new Rect(0f, num, 175f, 30f), localItem.kind?.defName ?? "Null"))
				{
					List<FloatMenuOption> list = new List<FloatMenuOption>();
					foreach (PawnKindDef kind in kindDefs)
					{
						PawnKindDef localKind = kind;
						list.Add(new FloatMenuOption(localKind.defName, delegate
						{
							localItem.kind = localKind;
						}));
					}
					Find.WindowStack.Add(new FloatMenu(list));
				}
				string buffer = localItem.selectionWeight.ToString();
				string text = Widgets.TextField(new Rect(175, num, width - 205, 30f), buffer, 5);
				if (text != buffer && IsPartiallyOrFullyTypedNumber(text))
				{
					buffer = text;
					if (IsFullyTypedNumber(text) && float.TryParse(text, out var result))
					{
						localItem.selectionWeight = result;
					}
				}
				if (Widgets.ButtonImageFitted(new Rect(width - 30f, num, 30f, 30f), DevGUI.CheckOff))
				{
					tool.options.Remove(localItem);
				}
				num += 30f;
			}
			if (Widgets.ButtonText(new Rect(0f, num, 100f, 30f), "Add"))
			{
				if (kindDefs.NullOrEmpty())
				{
					RegeneratePawnKinds(tool.factionDef);
				}
				List<FloatMenuOption> list = new List<FloatMenuOption>();
				foreach (PawnKindDef kind in kindDefs)
				{
					PawnKindDef localKind = kind;
					list.Add(new FloatMenuOption(localKind.defName, delegate
					{
						tool.options.Add(new PawnGenOption() { kind = localKind, selectionWeight = 10f });
					}));
				}
				Find.WindowStack.Add(new FloatMenu(list));
			}
			Widgets.EndScrollView();
		}

		public void RegeneratePawnKinds(FactionDef faction = null)
		{
			kindDefs = DefDatabase<PawnKindDef>.AllDefs.Where((x) => AllowKind(x)).ToList();
			bool AllowKind(PawnKindDef kind)
			{
				if (kind.RaceProps.Animal)
				{
					return false;
				}
				if(faction == null)
				{
					return true;
				}
				if(kind.defaultFactionDef == faction || (!faction.categoryTag.NullOrEmpty() && kind.defaultFactionDef?.categoryTag == faction.categoryTag))
				{
					return true;
				}
				if (!faction.humanlikeFaction && kind.RaceProps.Humanlike)
				{
					return false;
				}
				if(faction.techLevel > TechLevel.Industrial && kind.RaceProps.IsMechanoid)
				{
					return true;
				}
				return false;
			}
		}

		private static bool IsPartiallyOrFullyTypedNumber(string s)
		{
			if (s == "")
			{
				return true;
			}
			if (s.Length > 1 && s[s.Length - 1] == '-')
			{
				return false;
			}
			if (s == "00")
			{
				return false;
			}
			if (s.Length > 12)
			{
				return false;
			}
			if (CharacterCount(s, '.') <= 1 && ContainsOnlyCharacters(s, "-.0123456789"))
			{
				return true;
			}
			if (IsFullyTypedNumber(s))
			{
				return true;
			}
			return false;
		}

		private static bool IsFullyTypedNumber(string s)
		{
			if (s == "")
			{
				return false;
			}
			string[] array = s.Split('.');
			if (array.Length > 2 || array.Length < 1)
			{
				return false;
			}
			if (!ContainsOnlyCharacters(array[0], "-0123456789"))
			{
				return false;
			}
			if (array.Length == 2 && (array[1].Length == 0 || !ContainsOnlyCharacters(array[1], "0123456789")))
			{
				return false;
			}
			return true;
		}

		private static bool ContainsOnlyCharacters(string s, string allowedChars)
		{
			for (int i = 0; i < s.Length; i++)
			{
				if (!allowedChars.Contains(s[i]))
				{
					return false;
				}
			}
			return true;
		}

		private static int CharacterCount(string s, char c)
		{
			int num = 0;
			for (int i = 0; i < s.Length; i++)
			{
				if (s[i] == c)
				{
					num++;
				}
			}
			return num;
		}
	}

	public class ExportTool_PawnGroup : ExportTool
	{
		public FactionDef factionDef;
		public string lordTag = "";
		public string elementTypeName = "Fortified.Structures.FFF_Element_PawnGroup";
		public float sendSignalRadius = -1f;
		private float minPoints = 1000;
		private float maxPoints = 1000;
		public bool fixedOptions = false;
		public FloatRange pointsRange = new FloatRange(1000, 1000);
		public List<PawnGenOption> options = new List<PawnGenOption>();

		private List<PawnKindDef> kindDefs;
		private List<float> weights;

		public void CopyFrom(ExportTool_PawnGroup tool)
		{
			elementTypeName = tool.elementTypeName;
			fixedOptions = tool.fixedOptions;
			factionDef = tool.factionDef;
			lordTag = tool.lordTag;
			pointsRange = tool.pointsRange;
			options = new List<PawnGenOption>();
			sendSignalRadius = tool.sendSignalRadius;
			foreach (var item in tool.options)
			{
				options.Add(new PawnGenOption() { kind = item.kind, selectionWeight = item.selectionWeight });
			}
		}

		public override IEnumerable<Gizmo> GetGizmos()
		{
			yield return new Command_Action
			{
				defaultLabel = "DEV: change props",
				action = delegate
				{
					if(!Find.WindowStack.Windows.Any((x)=>x is Dialog_PawnGroupExtractTool dialog && dialog.tool == this))
					{
						Find.WindowStack.Add(new Dialog_PawnGroupExtractTool(this));
					}
				}
			};
		}

		public override void DrawExtraSelectionOverlays()
		{
			base.DrawExtraSelectionOverlays();
			if(sendSignalRadius > 0)
			{
				GenDraw.DrawRadiusRing(Position, Mathf.Min(sendSignalRadius, GenRadial.MaxRadialPatternRadius));
			}
		}
		public override void ExportToXML(IntVec3 origin, StringBuilder sb)
		{
			if(options.NullOrEmpty())
			{
				return;
			}
			sb.AppendLine("      <li Class=\"" + elementTypeName + "\">");
			if(factionDef != null)
			{
				sb.AppendLine($"        <factionDef>{factionDef.defName}</factionDef>");
			}
			sb.AppendLine($"        <sendSignalRadius>{sendSignalRadius}</sendSignalRadius>");
			IntVec3 pos = Position - origin;
			sb.AppendLine($"        <pos>({pos.x}, 0, {pos.z})</pos>");
			if (fixedOptions)
			{
				sb.AppendLine("        <fixedOptions>true</fixedOptions>");
			}
			else
			{
				sb.AppendLine($"        <pointsRange>{pointsRange.min}~{pointsRange.max}</pointsRange>");
			}
			if (!lordTag.NullOrEmpty()) sb.AppendLine($"        <lordTag>{lordTag.ToString()}</lordTag>");
			sb.AppendLine("        <options>");
			foreach (var item in options)
			{
				sb.AppendLine($"          <{item.kind.defName}>{item.selectionWeight}</{item.kind.defName}>");
			}
			sb.AppendLine("        </options>");
			sb.AppendLine("      </li>");
		}

		public override void ExposeData()
		{
			base.ExposeData();
			if (Scribe.mode == LoadSaveMode.Saving)
			{
				if (!options.NullOrEmpty())
				{
					kindDefs = new List<PawnKindDef>();
					weights = new List<float>();
					foreach (var item in options)
					{
						kindDefs.Add(item.kind);
						weights.Add(item.selectionWeight);
					}
				}
				minPoints = pointsRange.min;
				maxPoints = pointsRange.max;
			}
			Scribe_Collections.Look(ref kindDefs, "kindDefs", LookMode.Def);
			Scribe_Collections.Look(ref weights, "weights", LookMode.Value);
			Scribe_Values.Look(ref lordTag, "lordTag");
			Scribe_Values.Look(ref elementTypeName, "elementTypeName");
			Scribe_Values.Look(ref sendSignalRadius, "sendSignalRadius", defaultValue: -1);
			Scribe_Values.Look(ref minPoints, "minPoints", 1000);
			Scribe_Values.Look(ref maxPoints, "maxPoints", 1000);
			Scribe_Values.Look(ref fixedOptions, "fixedOptions", defaultValue: false);
			Scribe_Defs.Look(ref factionDef, "factionDef");
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				pointsRange = new FloatRange(minPoints, maxPoints);
				if (!kindDefs.NullOrEmpty())
				{
					options = new List<PawnGenOption>();
					for (int i = 0; i < kindDefs.Count; i++)
					{
						if (kindDefs[i] != null)
						{
							options.Add(new PawnGenOption() { kind = kindDefs[i], selectionWeight = weights[i] });
						}
					}
					kindDefs = null;
					weights = null;
				}
			}
		}
	}

	public class Dialog_RandomStructureExtractTool : Window
	{
		public override Vector2 InitialSize => new Vector2(500f, 150f);

		public ExportTool_RandomStructure tool;

		public Dialog_RandomStructureExtractTool(ExportTool_RandomStructure tool)
		{
			this.doCloseButton = true;
			forcePause = false;
			absorbInputAroundWindow = false;
			onlyOneOfTypeAllowed = false;
			this.draggable = true;
			this.tool = tool;
			this.closeOnClickedOutside = false;
			this.preventCameraMotion = false;
		}

		public override void DoWindowContents(Rect inRect)
		{
			Text.Font = GameFont.Small;
			tool.substructureTag = Widgets.TextField(new Rect(inRect.x, inRect.y, 500f, 30f), tool.substructureTag);
		}
	}

	public class ExportTool_RandomStructure : ExportTool
	{
		public string substructureTag = "";

		public override IEnumerable<Gizmo> GetGizmos()
		{
			yield return new Command_Action
			{
				defaultLabel = "DEV: change props",
				action = delegate
				{
					if (!Find.WindowStack.Windows.Any((x) => x is Dialog_RandomStructureExtractTool dialog && dialog.tool == this))
					{
						Find.WindowStack.Add(new Dialog_RandomStructureExtractTool(this));
					}
				}
			};
		}

		public override void ExportToXML(IntVec3 origin, StringBuilder sb)
		{
			if (substructureTag.NullOrEmpty())
			{
				return;
			}
			IntVec3 pos = Position - origin;
			sb.AppendLine("      <li Class=\"Fortified.Structures.FFF_Element_RandomSubStructure\">");
			sb.AppendLine($"        <pos>{pos}</pos>");
			sb.AppendLine($"        <tag>{substructureTag}</tag>");
			sb.AppendLine($"        <chance>{1}</chance>");
			sb.AppendLine("      </li>");
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref substructureTag, "substructureTag");
		}
	}

	public class ExportTool_LinkAccessKeyWanter : ExportTool
	{
		public IntVec3 wanter = IntVec3.Invalid;

		public IntVec3 activatable = IntVec3.Invalid;

		public override IEnumerable<Gizmo> GetGizmos()
		{
			yield return new Command_Action
			{
				defaultLabel = "DEV: select wanter",
				defaultDesc = "Select a cell containing an IAccessKeyWanter (Thing or ThingComp).",
				action = TargetWanter
			};
			yield return new Command_Action
			{
				defaultLabel = "DEV: select activatable",
				defaultDesc = "Select a cell containing an IAccessKeyActivatable (Thing or ThingComp).",
				action = TargetActivatable
			};
			yield return new Command_Action
			{
				defaultLabel = "DEV: clear link",
				defaultDesc = "Clear both endpoints.",
				action = delegate
				{
					wanter = IntVec3.Invalid;
					activatable = IntVec3.Invalid;
				}
			};
		}

		public void TargetActivatable()
		{
			BeginCellTargeting("Select activatable", AccessKeyLinkUtility.HasActivatableAt, delegate (IntVec3 c)
			{
				activatable = c;
			});
		}

		public void TargetWanter()
		{
			BeginCellTargeting("Select access wanter", AccessKeyLinkUtility.HasWanterAt, delegate (IntVec3 c)
			{
				wanter = c;
			});
		}

		/// <summary>
		/// 共用的選格流程。<paramref name="cellValidator"/> 只透過 AccessKeyLinkUtility 判斷，
		/// 因此本工具不認得任何具體 Comp 型別。
		/// </summary>
		private void BeginCellTargeting(string label, Func<IntVec3, Map, bool> cellValidator, Action<IntVec3> setter)
		{
			Map map = Map;
			if (map == null || cellValidator == null || setter == null)
			{
				return;
			}

			bool ValidateTarget(LocalTargetInfo t)
			{
				return t.IsValid && t.Cell.IsValid && t.Cell.InBounds(map) && cellValidator(t.Cell, map);
			}

			Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), delegate (LocalTargetInfo t)
			{
				if (ValidateTarget(t))
				{
					setter(t.Cell);
				}
			}, delegate (LocalTargetInfo t)
			{
				if (ValidateTarget(t))
				{
					GenDraw.DrawTargetHighlight(t);
					GenDraw.DrawFieldEdges(new List<IntVec3>() { t.Cell });
				}
			}, ValidateTarget, null, null, BaseContent.ClearTex, playSoundOnAction: true, delegate (LocalTargetInfo t)
			{
				Widgets.MouseAttachedLabel(label);
			});
		}

		/// <summary>兩端是否都仍指向合法的實作。地圖被改動後可能失效。</summary>
		public bool LinkIsValid
		{
			get
			{
				Map map = Map;
				if (map == null || !activatable.IsValid || !wanter.IsValid)
				{
					return false;
				}
				return AccessKeyLinkUtility.HasActivatableAt(activatable, map) && AccessKeyLinkUtility.HasWanterAt(wanter, map);
			}
		}

		public override string GetInspectString()
		{
			StringBuilder sb = new StringBuilder(base.GetInspectString());
			if (sb.Length > 0)
			{
				sb.AppendLine();
			}
			Map map = Map;
			bool hasActivatable = map != null && activatable.IsValid && AccessKeyLinkUtility.HasActivatableAt(activatable, map);
			bool hasWanter = map != null && wanter.IsValid && AccessKeyLinkUtility.HasWanterAt(wanter, map);
			sb.AppendLine("Activatable: " + (activatable.IsValid ? activatable.ToString() + (hasActivatable ? "" : " (missing!)") : "unset"));
			sb.Append("Wanter: " + (wanter.IsValid ? wanter.ToString() + (hasWanter ? "" : " (missing!)") : "unset"));
			return sb.ToString();
		}

		public override void DrawExtraSelectionOverlays()
		{
			base.DrawExtraSelectionOverlays();
			if(wanter.IsValid)
			{
				GenDraw.DrawLineBetween(this.TrueCenter(), wanter.ToVector3Shifted(), SimpleColor.Yellow);
			}
			if (activatable.IsValid)
			{
				GenDraw.DrawLineBetween(this.TrueCenter(), activatable.ToVector3Shifted(), SimpleColor.Cyan);
			}
			if (activatable.IsValid && wanter.IsValid)
			{
				GenDraw.DrawLineBetween(activatable.ToVector3Shifted(), wanter.ToVector3Shifted(), SimpleColor.Green);
			}
		}

		public override void ExportToXML(IntVec3 origin, StringBuilder sb)
		{
			if (!activatable.IsValid || !wanter.IsValid)
			{
				return;
			}
			// 選取後地圖可能被改動，匯出前重新以介面驗證兩端；失效時出聲而非靜默丟棄
			if (!LinkIsValid)
			{
				Log.Warning($"[FFF] ExportTool_LinkAccessKeyWanter at {Position}: endpoint no longer implements IAccessKeyActivatable / IAccessKeyWanter, skipped.");
				return;
			}
			IntVec3 activatablePos = activatable - origin;
			IntVec3 wanterPos = wanter - origin;
			sb.AppendLine("      <li Class=\"Fortified.Structures.FFF_Element_LinkAccessKeyWanter\">");
			sb.AppendLine($"        <wanterPos>{wanterPos}</wanterPos>");
			sb.AppendLine($"        <activatablePos>{activatablePos}</activatablePos>");
			sb.AppendLine("      </li>");
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref activatable, "activatable");
			Scribe_Values.Look(ref wanter, "wanter");
		}
	}

	public class ExportTool_ManTurret : ExportTool
	{
		public PawnKindDef kindDef;

		public override IEnumerable<Gizmo> GetGizmos()
		{
			yield return new Command_Action
			{
				defaultLabel = "DEV: change props",
				action = delegate
				{
					List<FloatMenuOption> list = new List<FloatMenuOption>();
					foreach (PawnKindDef kind in DefDatabase<PawnKindDef>.AllDefs.Where((x) => AllowKind(x)))
					{
						PawnKindDef localKind = kind;
						list.Add(new FloatMenuOption(localKind.defName, delegate
						{
							kindDef = localKind;
						}));
					}
					Find.WindowStack.Add(new FloatMenu(list));
					bool AllowKind(PawnKindDef kind)
					{
						if (kind.RaceProps.Animal)
						{
							return false;
						}
						return true;
					}
				}
			};
		}

		public override void Notify_DebugSpawned()
		{
			base.Notify_DebugSpawned();
			foreach(IntVec3 c in CellRect.FromCell(Position).ExpandedBy(1))
			{
				Building_TurretGun turret = c.GetFirstThing<Building_TurretGun>(Map);
				if(turret != null)
				{
					Position = turret.Position;
					return;
				}
			}
		}

		public override void ExportToXML(IntVec3 origin, StringBuilder sb)
		{
			IntVec3 pos = Position - origin;
			sb.AppendLine("      <li Class=\"Fortified.Structures.FFF_Element_ManMortar\">");
			sb.AppendLine($"        <pos>{pos}</pos>");
			if(kindDef != null)
			{
				sb.AppendLine($"        <kindDef>{kindDef.defName}</kindDef>");
			}
			sb.AppendLine($"        <chance>{1}</chance>");
			sb.AppendLine("      </li>");
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Defs.Look(ref kindDef, "kindDef");
		}
	}
}
