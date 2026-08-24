using DelaunatorSharp;
using Fortified;
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
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
using static RimWorld.MechClusterSketch;

namespace Fortified
{
	public class OverseerMech : WeaponUsableMech, IOverseerMech
	{
		public bool ControllableByState => true;

		private CompOverseer comp;

		private MechWorkModeDef workMode;

		public float minCharge = 0.05f;

		public float maxCharge = 1f;

		public CompOverseer Comp
        {
            get
            {
                if(comp == null)
                {
                    comp = GetComp<CompOverseer>();
                }
                return comp;
            }
        }

		public MechWorkModeDef WorkMode
		{
			get
			{
				if (workMode == null)
				{
					workMode = MechWorkModeDefOf.Work;
				}
				return workMode;
			}
			set
			{
				if (value == workMode)
				{
					return;
				}
				workMode = value;
				PawnComponentsUtility.AddAndRemoveDynamicComponents(this, actAsIfSpawned: true);
				if (workMode != MechWorkModeDefOf.Recharge && CurJobDef == JobDefOf.MechCharge && this.IsCharging())
				{
					jobs.EndCurrentJob(JobCondition.InterruptForced);
				}
				GetComp<CompCanBeDormant>()?.WakeUp();
				jobs?.CheckForJobOverride();
			}
		}

		public override bool CanCaravan => true;

		public float MinCharge { get => minCharge; set => minCharge = Mathf.Clamp01(value); }

		public float MaxCharge { get => maxCharge; set => maxCharge = Mathf.Clamp01(value); }

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

        public virtual void Notify_NameChanged()
        {
			if (Name?.IsValid == true && Comp?.dummyPawn != null)
			{
				Comp.dummyPawn.Name = Name;
			}
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Defs.Look(ref workMode, "FFFOverseer_workMode");
			Scribe_Values.Look(ref minCharge, "FFFOverseer_minCharge", defaultValue: 0.05f);
			Scribe_Values.Look(ref maxCharge, "FFFOverseer_maxCharge", defaultValue: 1f);
		}

		public override IEnumerable<Gizmo> GetGizmos()
		{
			if (Comp.MechanitorActive)
			{
				if (!Drafted)
				{
					yield return new OverseerMechGizmo(this);
				}
				foreach (Gizmo g in comp.dummyPawn.mechanitor.GetGizmos())
				{
					if (g is Command_CallBossgroup)
					{
						continue;
					}
					yield return g;
				}
			}
			foreach (Gizmo g in base.GetGizmos())
			{
				yield return g;
			}
		}
    }

	public class HumanlikeOverseerMech : HumanlikeMech, IOverseerMech
	{
		public bool ControllableByState => true;

		private CompOverseer comp;

		private MechWorkModeDef workMode;

		public float minCharge = 0.05f;

		public float maxCharge = 1f;

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

		public MechWorkModeDef WorkMode
		{
			get
			{
				if (workMode == null)
				{
					workMode = MechWorkModeDefOf.Work;
				}
				return workMode;
			}
			set
			{
				if (value == workMode)
				{
					return;
				}
				workMode = value;
				PawnComponentsUtility.AddAndRemoveDynamicComponents(this, actAsIfSpawned: true);
				if (workMode != MechWorkModeDefOf.Recharge && CurJobDef == JobDefOf.MechCharge && this.IsCharging())
				{
					jobs.EndCurrentJob(JobCondition.InterruptForced);
				}
				GetComp<CompCanBeDormant>()?.WakeUp();
				jobs?.CheckForJobOverride();
			}
		}

		public override bool CanCaravan => true;

		public float MinCharge { get => minCharge; set => minCharge = Mathf.Clamp01(value); }

		public float MaxCharge { get => maxCharge; set => maxCharge = Mathf.Clamp01(value); }

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

		public virtual void Notify_NameChanged()
		{
			if (Name?.IsValid == true && Comp?.dummyPawn != null)
			{
				Comp.dummyPawn.Name = Name;
			}
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Defs.Look(ref workMode, "FFFOverseer_workMode");
			Scribe_Values.Look(ref minCharge, "FFFOverseer_minCharge", defaultValue: 0.05f);
			Scribe_Values.Look(ref maxCharge, "FFFOverseer_maxCharge", defaultValue: 1f);
		}

		public override IEnumerable<Gizmo> GetGizmos()
		{
			if (Comp.MechanitorActive)
			{
				if (!Drafted)
				{
					yield return new OverseerMechGizmo(this);
				}
				foreach (Gizmo g in comp.dummyPawn.mechanitor.GetGizmos())
				{
					if (g is Command_CallBossgroup)
					{
						continue;
					}
					yield return g;
				}
			}
			foreach (Gizmo g in base.GetGizmos())
			{
				yield return g;
			}
		}
	}
}