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
using static UnityEngine.GraphicsBuffer;

namespace Fortified
{
    public class CompProperties_Overseer : CompProperties
    {
        /*public float commandRange = 34.9f;

        public int controlGroups = 2;

        public int bandwidth = 6;*/

        public bool canRepair = true;

        public int ticksPerHeal = 120;

		public bool instantControl = false;

        public bool controlWholeMap = false;

		[NoTranslate]
        public string selectOverseerIconPath = "UI/Icons/SelectOverseer";

        public CompProperties_Overseer()
        {
            compClass = typeof(CompOverseer);
        }

		public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
		{
			foreach (string item in base.ConfigErrors(parentDef))
			{
				yield return item;
			}
            if (!ModsConfig.BiotechActive)
            {
				yield return "Fortified.CompOverseer require Biotech to work";
			}
		}

		public override IEnumerable<StatDrawEntry> SpecialDisplayStats(StatRequest req)
		{
			if (req.HasThing)
            {
                yield return GetEntry(StatDefOf.MechBandwidth, req.Thing.GetStatValue(StatDefOf.MechBandwidth));
                yield return GetEntry(StatDefOf.MechControlGroups, req.Thing.GetStatValue(StatDefOf.MechControlGroups));
                //yield return GetEntry(FFF_DefOf.FFF_MechCommandRange, req.Thing.GetStatValue(FFF_DefOf.FFF_MechCommandRange));
			}
            else if(req.Def is ThingDef t)
            {
				yield return GetEntry(StatDefOf.MechBandwidth, t.GetStatValueAbstract(StatDefOf.MechBandwidth));
				yield return GetEntry(StatDefOf.MechControlGroups, t.GetStatValueAbstract(StatDefOf.MechControlGroups));
				//yield return GetEntry(FFF_DefOf.FFF_MechCommandRange, t.GetStatValueAbstract(FFF_DefOf.FFF_MechCommandRange));
			}
            StatDrawEntry GetEntry(StatDef def, float value)
            {
                return new StatDrawEntry(def.category, def, value, req);
            }
		}
    }

    public class CompOverseer : ThingComp
    {
        public CompProperties_Overseer Props => (CompProperties_Overseer)props;

		private float? cachedCommandRange = null;

		private int? cachedBandwidth = null;

		public Pawn dummyPawn;

        public bool activeInt = true;

		public bool MechanitorActive => activeInt && dummyPawn != null && parent.Faction == Faction.OfPlayerSilentFail;

        public int CurrentBandwidth
        {
            get
            {
                if(cachedBandwidth == null)
                {
					cachedBandwidth = (int)parent.GetStatValue(StatDefOf.MechBandwidth);
				}
				return cachedBandwidth.Value;
            }
        }

        public float CurrentCommandRange
        {
            get
            {
                if(cachedCommandRange == null)
                {
					cachedCommandRange = parent.GetStatValue(FFF_DefOf.FFF_MechCommandRange);
				}
                return cachedCommandRange.Value;
			}
        }

        private Texture2D selectIcon;
		public Texture2D SelectIcon
		{
			get
			{
				if (selectIcon == null)
				{
					selectIcon = ContentFinder<Texture2D>.Get(Props.selectOverseerIconPath);
				}
				return selectIcon;
			}
		}

        public void Notify_BandwidthChanged()
        {
            dummyPawn?.mechanitor.Notify_BandwidthChanged();
        }

		public void RecacheValues()
        {
            cachedCommandRange = null;
            cachedBandwidth = null;
			Hediff_Dummy hediff = (Hediff_Dummy)dummyPawn.health.GetOrAddHediff(FFF_DefOf.FFF_DummyHediff);
			hediff.overseer = parent as IOverseer;
			hediff.Severity = Mathf.Max((int)parent.GetStatValue(StatDefOf.MechControlGroups) - 2, 0.1f);
		}

		public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
			if (parent.Faction == Faction.OfPlayerSilentFail)
			{
				UpdateDummy();
			}
			if (parent is Pawn pawn)
            {
				pawn.GetOverseer()?.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, pawn);
			}
        }

        public override void PostDraw()
        {
            base.PostDraw();
            if (MechanitorActive && parent.Spawned && CurrentCommandRange < GenRadial.MaxRadialPatternRadius && dummyPawn.mechanitor.AnySelectedDraftedMechs)
            {
                GenDraw.DrawRadiusRing(parent.Position, CurrentCommandRange, Color.white, (IntVec3 c) => c.InBounds(parent.MapHeld));
            }
        }

        public void UpdateDummy()
        {
            if (dummyPawn is null)
            {
                PawnGenerationRequest req = new PawnGenerationRequest(FFF_DefOf.FFF_Dummy, Faction.OfAncients, forcedXenotype: XenotypeDefOf.Baseliner, forceGenerateNewPawn: true);
                dummyPawn = PawnGenerator.GeneratePawn(req);
                dummyPawn.SetFactionDirect(parent.Faction);
                dummyPawn.Name = new NameSingle(parent.LabelCap);
                for (int num = dummyPawn.health.hediffSet.hediffs.Count - 1; num >= 0; num--)
                {
                    Hediff h = dummyPawn.health.hediffSet.hediffs.FirstOrDefault((Hediff x) => x.def != FFF_DefOf.FFF_DummyHediff && !(x is Hediff_Mechlink));
                    if(h == null)
                    {
                        break;
                    }
                    dummyPawn.health.RemoveHediff(h);
                }
            }
            Hediff_Dummy hediff = (Hediff_Dummy)dummyPawn.health.GetOrAddHediff(FFF_DefOf.FFF_DummyHediff);
            hediff.overseer = parent as IOverseer;
            hediff.Severity = Mathf.Max((int)parent.GetStatValue(StatDefOf.MechControlGroups) - 2, 0.1f);
			PawnComponentsUtility.AddComponentsForSpawn(dummyPawn);
            PawnComponentsUtility.AddAndRemoveDynamicComponents(dummyPawn);
            dummyPawn.mechanitor.Notify_BandwidthChanged();
            dummyPawn.gender = Gender.None;
            dummyPawn.equipment.DestroyAllEquipment();
            dummyPawn.story.title = "";
        }

        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)
        {
            if (dummyPawn != null)
            {
                dummyPawn.mechanitor.UndraftAllMechs();
                List<Pawn> list = dummyPawn.mechanitor.OverseenPawns.ToList();
                foreach (Pawn p in list)
                {
                    dummyPawn.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, p);
                }
                dummyPawn.mechanitor.Notify_BandwidthChanged();
            }
            base.Notify_Killed(prevMap, dinfo);
        }

		public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
            if (parent.Faction != Faction.OfPlayerSilentFail)
            {
                yield break;
            }
            if (dummyPawn == null)
            {
                UpdateDummy();
                yield break;
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.IsHashIntervalTick(240) || !MechanitorActive)
            {
                return;
            }
            if (dummyPawn.Faction != parent.Faction)
            {
                dummyPawn.SetFaction(parent.Faction);
            }
            if(parent is Pawn p)
            {
				Pawn pawn = p.GetOverseer();
				if (pawn != null)
				{
					pawn.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, p);
					pawn.mechanitor.Notify_BandwidthChanged();
				}
			}
        }

        public void Connect(Pawn mech)
        {
            if (mech.Faction != dummyPawn.Faction)
            {
                mech.SetFaction(dummyPawn.Faction);
            }
            mech.GetOverseer()?.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, mech);
			dummyPawn.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
			dummyPawn.mechanitor.Notify_BandwidthChanged();
        }

        public override void Notify_Downed()
        {
            base.Notify_Downed();
            dummyPawn?.mechanitor?.UndraftAllMechs();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref dummyPawn, "FFF_dummyPawn");
            Scribe_Values.Look(ref activeInt, "FFF_activeInt", defaultValue: true);
		}
    }
}