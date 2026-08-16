using DelaunatorSharp;
using Gilzoide.ManagedJobs;
using HarmonyLib;
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
using Verse.AI.Group;
using Verse.Grammar;
using Verse.Noise;
using Verse.Profile;
using Verse.Sound;
using Verse.Steam;
using static HarmonyLib.Code;

namespace Fortified
{
	public static class OverseerUtility
	{
		public static IOverseer GetParentOverseer(this Pawn dummy)
		{
			if (dummy == null || dummy.mechanitor == null || dummy.kindDef != FFF_DefOf.FFF_Dummy)
			{
				return null;
			}
			return dummy.health?.hediffSet?.GetFirstHediff<Hediff_Dummy>()?.overseer;
		}

		public static CompOverseer GetOverseerComp(this Pawn dummy)
		{
			return GetParentOverseer(dummy)?.Comp;
		}

		public static Thing GetOverseerThing(this Pawn dummy)
		{
			return GetParentOverseer(dummy) as Thing;
		}

		public static Thing GetOverseerThing(this Pawn dummy, out IOverseer overseer)
		{
			overseer = GetParentOverseer(dummy);
			return overseer as Thing;
		}

		public static Pawn GetOverseerPawn(this Pawn dummy)
		{
			return GetParentOverseer(dummy) as Pawn;
		}

		public static Pawn GetOverseerPawn(this Pawn dummy, out IOverseer overseer)
		{
			overseer = GetParentOverseer(dummy);
			return overseer as Pawn;
		}

		public static Pawn GetOverseerMech(this Pawn dummy, out IOverseerMech overseer)
		{
			overseer = GetParentOverseer(dummy) as IOverseerMech;
			return overseer as Pawn;
		}
	}
}