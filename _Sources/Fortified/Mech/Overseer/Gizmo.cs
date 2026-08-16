using DelaunatorSharp;
using Gilzoide.ManagedJobs;
using Ionic.Crc;
using Ionic.Zlib;
using JetBrains.Annotations;
using KTrie;
using LudeonTK;
using NVorbis.NAudioSupport;
using RimWorld;
using RimWorld.BaseGen;
using RimWorld.IO;
using RimWorld.Planet;
using RimWorld.QuestGen;
using RimWorld.SketchGen;
using RimWorld.Utility;
using RuntimeAudioClipLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using System.Xml.XPath;
using System.Xml.Xsl;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Verse;
using Verse.AI;
using Verse.Grammar;
using Verse.Sound;
using static HarmonyLib.Code;
using static RimWorld.MechClusterSketch;
using static Unity.IO.LowLevel.Unsafe.AsyncReadManagerMetrics;
using static UnityEngine.Scripting.GarbageCollector;

namespace Fortified
{
	public class OverseerBuildingBandwidthGizmo : MechanitorBandwidthGizmo
	{
		public override bool Visible => true;

		public OverseerBuildingBandwidthGizmo(Pawn_MechanitorTracker tracker) : base(tracker)
		{

		}
	}

	public class OverseerMechGizmo : Gizmo
	{
		public const int InRectPadding = 6;

		private const float Width = 130f;

		private const int IconButtonSize = 26;

		private const float BaseSelectedTexJump = 20f;

		private const float BaseSelectedTextScale = 0.8f;

		private static readonly CachedTexture PowerIcon = new CachedTexture("UI/Icons/MechRechargeSettings");

		private static readonly Color UncontrolledMechBackgroundColor = new Color32(byte.MaxValue, 25, 25, 55);

		private IOverseerMech mech;

		public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions => GetWorkModeOptions(mech);

		public override bool Visible
		{
			get
			{
				return Find.Selector.SelectedPawns.Count == 1;
			}
		}

		public override float Order
		{
			get
			{
				return -90f;
			}
		}

		public OverseerMechGizmo(IOverseerMech mech)
		{
			this.mech = mech;
			Order = -90f;
		}

		public IEnumerable<FloatMenuOption> GetWorkModeOptions(IOverseerMech mech)
		{
			foreach (MechWorkModeDef wm in DefDatabase<MechWorkModeDef>.AllDefsListForReading.OrderBy((MechWorkModeDef d) => d.uiOrder))
			{
				MechWorkModeDef wmLocal = wm;
				FloatMenuOption floatMenuOption = new FloatMenuOption(wmLocal.LabelCap, delegate
				{
					mech.WorkMode = wmLocal;
				}, wmLocal.uiIcon, Color.white);
				floatMenuOption.tooltip = new TipSignal(wmLocal.description, wmLocal.index ^ 0xDFE8661);
				yield return floatMenuOption;
			}
		}

		public override bool GroupsWith(Gizmo other)
		{
			return false;
		}

		public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
		{
			Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
			Rect inRect = rect.ContractedBy(6f);
			Widgets.DrawWindowBackground(rect);
			Rect rect1 = new Rect(inRect.x, inRect.y, 26f, 26f);
			Widgets.DrawTextureFitted(rect1, PowerIcon.Texture, 1f);
			if (!disabled && Mouse.IsOver(rect1))
			{
				Widgets.DrawHighlight(rect1);
				if (Widgets.ButtonInvisible(rect1))
				{
					Find.WindowStack.Add(new Dialog_OverseerRechargeSettings(mech));
				}
			}
			Rect rect2 = new Rect(inRect.x, inRect.yMax - 26f, 26f, 26f);
			Widgets.DrawTextureFitted(rect2, mech.WorkMode.uiIcon, 1f);
			if (!disabled && Mouse.IsOver(rect2))
			{
				Widgets.DrawHighlight(rect2);
				if (Widgets.ButtonInvisible(rect2))
				{
					Find.WindowStack.Add(new FloatMenu(GetWorkModeOptions(mech).ToList()));
				}
				if(Find.WindowStack.FloatMenu == null)
				{
					TooltipHandler.TipRegion(rect2, new TipSignal(("CurrentMechWorkMode".Translate() + ": " + mech.WorkMode.LabelCap).Colorize(ColoredText.TipSectionTitleColor) + "\n" + mech.WorkMode.description + "\n\n" + "ClickToChangeWorkMode".Translate()));
				}
			}
			return new GizmoResult(GizmoState.Clear);
		}

		public override float GetWidth(float maxWidth)
		{
			return 38f;
		}
	}

	public class Dialog_OverseerRechargeSettings : Window
	{
		private FloatRange range;

		private IOverseerMech mech;

		private string title;

		private string text;

		private const float HeaderHeight = 30f;

		private const float SliderHeight = 30f;

		public override Vector2 InitialSize => new Vector2(450f, 300f);

		public Dialog_OverseerRechargeSettings(IOverseerMech mech)
		{
			this.mech = mech;
			range = new FloatRange(mech.MinCharge, mech.MaxCharge);
			title = "MechRechargeSettingsTitle".Translate();
			text = "MechRechargeSettingsExplanation".Translate();
			forcePause = true;
			closeOnClickedOutside = true;
		}

		public override void DoWindowContents(Rect inRect)
		{
			float y = inRect.y;
			Rect rect = new Rect(inRect.x, y, inRect.width, 30f);
			Text.Font = GameFont.Medium;
			Text.Anchor = TextAnchor.MiddleLeft;
			Widgets.Label(rect, title);
			Text.Anchor = TextAnchor.UpperLeft;
			Text.Font = GameFont.Small;
			y += rect.height + 17f;
			Rect rect2 = new Rect(inRect.x, y, inRect.width, Text.CalcHeight(text, inRect.width));
			Text.Anchor = TextAnchor.MiddleLeft;
			Text.Font = GameFont.Small;
			Widgets.Label(rect2, text);
			Text.Anchor = TextAnchor.UpperLeft;
			y += rect2.height + 17f;
			Widgets.FloatRange(new Rect(inRect.x, y, inRect.width, 30f), GetHashCode(), ref range, 0f, 1f, null, ToStringStyle.PercentZero, 0.05f, GameFont.Small, Color.white);
			range.min = GenMath.RoundTo(range.min, 0.01f);
			range.max = GenMath.RoundTo(range.max, 0.01f);
			if (Widgets.ButtonText(new Rect(inRect.x, inRect.yMax - Window.CloseButSize.y, Window.CloseButSize.x, Window.CloseButSize.y), "CancelButton".Translate()))
			{
				Close();
			}
			if (Widgets.ButtonText(new Rect(inRect.x + inRect.width / 2f - Window.CloseButSize.x / 2f, inRect.yMax - Window.CloseButSize.y, Window.CloseButSize.x, Window.CloseButSize.y), "Reset".Translate()))
			{
				range = MechanitorControlGroup.DefaultMechRechargeThresholds;
			}
			if (Widgets.ButtonText(new Rect(inRect.xMax - Window.CloseButSize.x, inRect.yMax - Window.CloseButSize.y, Window.CloseButSize.x, Window.CloseButSize.y), "OK".Translate()))
			{
				mech.MinCharge = range.min;
				mech.MaxCharge = range.max;
				Close();
			}
		}
	}
}