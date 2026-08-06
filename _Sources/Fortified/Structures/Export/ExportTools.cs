using LudeonTK;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace Fortified.Structures
{
	public abstract class ExportTool : Thing
	{
		public abstract void ExportToXML(IntVec3 origin, StringBuilder sb);
	}

	public class Dialog_PawnGroupExtractTool : Window
	{
		private Vector2 scrollPosition = Vector2.zero;

		public override Vector2 InitialSize => new Vector2(400f, 500f);

		public ExportTool_PawnGroup tool;

		private List<PawnKindDef> kindDefs = new List<PawnKindDef>();

		public Dialog_PawnGroupExtractTool(ExportTool_PawnGroup tool)
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
			if (Widgets.ButtonText(new Rect(inRect.x, inRect.y, 300f, 30f), tool.elementTypeName.NullOrEmpty() ? "None" : tool.elementTypeName))
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
			if (Widgets.ButtonText(new Rect(inRect.x, inRect.y + 30f, 300f, 30f), tool.factionDef?.defName ?? "Null"))
			{
				List<FloatMenuOption> list = new List<FloatMenuOption>();
				foreach (FactionDef def in DefDatabase<FactionDef>.AllDefs)
				{
					FactionDef localDef = def;
					list.Add(new FloatMenuOption(localDef.defName, delegate
					{
						tool.factionDef = localDef;
						kindDefs = DefDatabase<PawnKindDef>.AllDefs.Where((x) => (!x.RaceProps.Humanlike || localDef.humanlikeFaction) && (localDef == x.defaultFactionDef || x.RaceProps.IsMechanoid)).ToList();
					}));
				}
				Find.WindowStack.Add(new FloatMenu(list));
			}
			Widgets.FloatRange(new Rect(inRect.x, inRect.y + 60f, 300f, 30f), GetHashCode(), ref tool.pointsRange, 0f, 10000f, "Points range " + tool.pointsRange.ToString(), ToStringStyle.Integer, 1f, roundTo: 100f);
			tool.lordTag = Widgets.TextField(new Rect(inRect.x, inRect.y + 90f, 200f, 30f), tool.lordTag, 20);
			if(Widgets.ButtonImage(new Rect(inRect.x + 200f, inRect.y + 90f, 30f, 30f), TexButton.Copy))
			{
				ExportTool_PawnGroup tool2 = (ExportTool_PawnGroup)Find.Selector.SelectedObjects.Where((object x) => x is ExportTool_PawnGroup t && t != tool).FirstOrDefault();
				if(tool2 != null)
				{
					tool.CopyFrom(tool2);
				}
			}
			if (Widgets.ButtonImage(new Rect(inRect.x + 230f, inRect.y + 90f, 30f, 30f), TexButton.Paste))
			{
				foreach (ExportTool_PawnGroup t in Find.Selector.SelectedObjects.Where((object x) => x is ExportTool_PawnGroup t && t != tool))
				{
					t.CopyFrom(tool);
				}
			}
			tool.sendSignalRadius = Widgets.HorizontalSlider(new Rect(inRect.x, inRect.y + 120f, inRect.width, 30f), tool.sendSignalRadius, -1f, 80, roundTo: 1, label: $"Signal radius ({tool.sendSignalRadius})");
			Widgets.DrawLineHorizontal(inRect.x, inRect.y + 155f, 300f);
			Rect outRect = new Rect(inRect.x, inRect.y + 160f, inRect.width, inRect.height - 35f - 5f - inRect.y);
			float width = outRect.width - 16f;
			Rect viewRect = new Rect(0f, 0f, width, tool.options.Count * 30f);
			Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
			float num = 0f;
			foreach (var item in tool.options.ToList())
			{
				PawnGenOption localItem = item;
				if (Widgets.ButtonText(new Rect(0f, num, 175f, 30f), localItem.kind?.defName ?? "Null"))
				{
					List<FloatMenuOption> list = new List<FloatMenuOption>();
					list.Add(new FloatMenuOption("Remove", delegate
					{
						tool.options.Remove(localItem);
					}));
					foreach (PawnKindDef kind in kindDefs)
					{
						PawnKindDef localKind = kind;
						list.Add(new FloatMenuOption(localKind.defName, delegate
						{
							localItem.kind = kind;
						}));
					}
					Find.WindowStack.Add(new FloatMenu(list));
				}
				string buffer = localItem.selectionWeight.ToString();
				string text = Widgets.TextField(new Rect(175f, num, width - 175f, 30f), buffer, 5);
				if (text != buffer && IsPartiallyOrFullyTypedNumber(text))
				{
					buffer = text;
					if (IsFullyTypedNumber(text) && float.TryParse(text, out var result))
					{
						localItem.selectionWeight = result;
					}
				}
				num += 30f;
			}
			if (Widgets.ButtonText(new Rect(0f, num, 100f, 30f), "Add"))
			{
				if (kindDefs.NullOrEmpty())
				{
					kindDefs = DefDatabase<PawnKindDef>.AllDefs.Where((x) => (!x.RaceProps.Humanlike || tool.factionDef.humanlikeFaction) && (tool.factionDef == x.defaultFactionDef || x.RaceProps.IsMechanoid)).ToList();
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
		public FloatRange pointsRange = new FloatRange(1000, 1000);
		public List<PawnGenOption> options = new List<PawnGenOption>();

		private List<PawnKindDef> kindDefs;
		private List<float> weights;

		public void CopyFrom(ExportTool_PawnGroup tool)
		{
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
				GenDraw.DrawRadiusRing(Position, sendSignalRadius);
			}
		}
		public override void ExportToXML(IntVec3 origin, StringBuilder sb)
		{
			if(factionDef == null || options.NullOrEmpty())
			{
				return;
			}
			sb.AppendLine("      <li Class=\"" + elementTypeName + "\">");
			sb.AppendLine($"        <factionDef>{factionDef.defName}</factionDef>");
			sb.AppendLine($"        <sendSignalRadius>{sendSignalRadius}</sendSignalRadius>");
			IntVec3 pos = Position - origin;
			sb.AppendLine($"        <pos>({pos.x}, 0, {pos.z})</pos>");
			sb.AppendLine($"        <pointsRange>{pointsRange.min}~{pointsRange.max}</pointsRange>");
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
			sb.AppendLine("      <li Class=\"Fortified.Structures.FFF_Element_RandomSubStructure\">");
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
}
