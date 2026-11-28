using RimWorld;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Fortified
{
    public class TerrainDefPair : IExposable
    {
        public TerrainDef firstTerrain;

        public TerrainDef secondTerrain;

        public ThingDef resourceDefOverride;

        public IntRange? resourceCountRangeOverride;

        public TerrainDefPair()
        {
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref firstTerrain, "firstTerrain");
            Scribe_Defs.Look(ref secondTerrain, "secondTerrain");
            Scribe_Defs.Look(ref resourceDefOverride, "resourceDef");
            Scribe_Values.Look(ref resourceCountRangeOverride, "resourceCountRange");
        }

        public void LoadDataFromXmlCustom(XmlNode xmlRoot)
        {
            XmlHelper.ParseElements(this, xmlRoot, "firstTerrain", "secondTerrain");
        }

        public override string ToString()
        {
            return $"({((firstTerrain != null) ? firstTerrain.defName : "null")} : {((secondTerrain != null) ? secondTerrain.defName : "null")})";
        }

        public override int GetHashCode()
        {
            return firstTerrain.shortHash + secondTerrain.shortHash << 16;
        }
    }

    public class CompProperties_AffectTerrain : CompProperties
    {
        public float energyPerCast;

        public float radius = 5.9f;

        public float daysToRadius = 5f;

        public SoundDef soundWorking;

        public List<TerrainDefPair> terrainPairs = new List<TerrainDefPair>();

        public ThingDef resourceDef;

        public IntRange resourceCountRange;

        public int intervalRareTicks = 1;

        public CompProperties_AffectTerrain()
        {
            this.compClass = typeof(CompAffectTerrain);
        }
    }

    public class CompAffectTerrain : ThingComp
    {
        public CompProperties_AffectTerrain Props => (CompProperties_AffectTerrain)this.props;

        protected CompPowerTrader powerComp;

        protected CompRefuelable refuelableComp;

        protected CompBreakdownable breakdownableComp;

        private int progressTicks;

        private int intervalTicksLeft;

        private Sustainer sustainer;

        private float ProgressDays => (float)progressTicks / 60000f;

        protected float CurrentRadius => Mathf.Min(Props.radius, ProgressDays / Props.daysToRadius * Props.radius);

        protected bool Working
        {
            get
            {
                if (powerComp?.PowerOn == false)
                {
                    return false;
                }
                if (refuelableComp?.HasFuel == false)
                {
                    return false;
                }
                if (breakdownableComp?.BrokenDown == true)
                {
                    return false;
                }
                return true;
            }
        }

        private int TicksUntilRadiusInteger
        {
            get
            {
                float num = Mathf.Ceil(CurrentRadius) - CurrentRadius;
                if (num < 1E-05f)
                {
                    num = 1f;
                }
                float num2 = Props.radius / Props.daysToRadius;
                return (int)(num / num2 * 60000f);
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            progressTicks = 0;
            if (sustainer != null && !sustainer.Ended)
            {
                sustainer.End();
            }
        }

        public override void CompTickRare()
        {
            bool flag = Working;
            if (flag)
            {
                progressTicks += 250;
                intervalTicksLeft--;
                if (intervalTicksLeft <= 0)
                {
                    intervalTicksLeft = Props.intervalRareTicks;
                    int num = GenRadial.NumCellsInRadius(CurrentRadius);
                    for (int i = 0; i < num; i++)
                    {
                        if (TryAffectCell(parent.Map, parent.Position + GenRadial.RadialPattern[i], out TerrainDef terrain))
                        {
                            var terrainPair = Props.terrainPairs.FirstOrDefault(x => x.firstTerrain == terrain);
                            ThingDef resourceDef = terrainPair.resourceDefOverride ?? Props.resourceDef;
                            if (resourceDef != null)
                            {
                                Thing resource = ThingMaker.MakeThing(resourceDef);
                                int count = (terrainPair.resourceCountRangeOverride ?? Props.resourceCountRange).RandomInRange;
                                if (count > 0)
                                {
                                    resource.stackCount = count;
                                    GenPlace.TryPlaceThing(resource, parent.def.HasSingleOrMultipleInteractionCells ? parent.InteractionCell : parent.Position, parent.Map, ThingPlaceMode.Near);
                                }
                            }
                            break;
                        }
                    }
                }
            }
            if (flag && !Props.soundWorking.NullOrUndefined() && CurrentRadius < Props.radius - 0.0001f)
            {
                if (sustainer == null || sustainer.Ended)
                {
                    sustainer = Props.soundWorking.TrySpawnSustainer(SoundInfo.InMap(parent));
                }
                sustainer.Maintain();
            }
            else if (sustainer != null && !sustainer.Ended)
            {
                sustainer.End();
            }
        }

        public bool TryAffectCell(Map map, IntVec3 c, out TerrainDef usedTerrain)
        {
            TerrainDef terrainDef = map.terrainGrid.TopTerrainAt(c);
            TerrainDef terrainToDryTo = GetTerrainToDryTo(map, terrainDef);
            if (terrainToDryTo != null)
            {
                map.terrainGrid.SetTerrain(c, terrainToDryTo);
                usedTerrain = terrainDef;
                return true;
            }
            TerrainDef terrainDef2 = map.terrainGrid.UnderTerrainAt(c);
            if (terrainDef2 != null)
            {
                TerrainDef terrainToDryTo2 = GetTerrainToDryTo(map, terrainDef2);
                if (terrainToDryTo2 != null)
                {
                    map.terrainGrid.SetUnderTerrain(c, terrainToDryTo2);
                    usedTerrain = terrainDef2;
                    return true;
                }
            }
            usedTerrain = null;
            return false;
        }

        private TerrainDef GetTerrainToDryTo(Map map, TerrainDef terrainDef)
        {
            var pair = Props.terrainPairs.FirstOrDefault(x => x.firstTerrain == terrainDef);
            if (pair == null)
            {
                return null;
            }
            return pair.secondTerrain;
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref progressTicks, "progressTicks", 0);
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            if (CurrentRadius < Props.radius - 0.0001f)
            {
                GenDraw.DrawRadiusRing(parent.Position, CurrentRadius);
            }
        }

        public override string CompInspectStringExtra()
        {
            string text = "TimePassed".Translate().CapitalizeFirst() + ": " + progressTicks.ToStringTicksToPeriod() + "\n" + "CurrentRadius".Translate().CapitalizeFirst() + ": " + CurrentRadius.ToString("F1");
            if (ProgressDays < Props.daysToRadius && Working)
            {
                text += "\n" + "RadiusExpandsIn".Translate().CapitalizeFirst() + ": " + TicksUntilRadiusInteger.ToStringTicksToPeriod();
            }
            return text;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (DebugSettings.ShowDevGizmos)
            {
                Command_Action command_Action = new Command_Action();
                command_Action.defaultLabel = "DEV: Progress 1 day";
                command_Action.action = delegate
                {
                    progressTicks += 60000;
                };
                yield return command_Action;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            powerComp = parent.GetComp<CompPowerTrader>();
            refuelableComp = parent.GetComp<CompRefuelable>();
            breakdownableComp = parent.GetComp<CompBreakdownable>();
            base.PostSpawnSetup(respawningAfterLoad);
        }
    }
}
