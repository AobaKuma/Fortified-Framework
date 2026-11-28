using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified
{
    public class CompProperties_DockingClamp : CompProperties
    {
        public CompProperties_DockingClamp()
        {
            compClass = typeof(CompDockingClamp);
        }
    }

    public enum DockingClampStatus
    {
        Docked,
        Released,
        Unpowered,
        NotOnShip,
        NoGravship
    }

    /// <summary>
    /// 停泊夾具：轉移船與逆重飛船之間的臨時連接。夾具整個位於結構樁船體範圍內、通電且處於夾持狀態，
    /// 並且本身或四鄰有某艘逆重飛船的有效底板（<see cref="Building_GravEngine.ValidSubstructure"/>）時，
    /// 那艘逆重飛船的整片底板會併入船體範圍（<see cref="PileNetwork.Footprint"/>）一起轉移，
    /// 每格底板依 <see cref="TransferFlightTuningDef.dockedSubstructureMassPerCell"/> 加上質量。
    /// 連接不是結構：不必施工、隨時可放開，放開後逆重飛船照原版自行起降。
    /// </summary>
    public class CompDockingClamp : ThingComp
    {
        private static readonly List<Building_GravEngine> tmpEngines = new List<Building_GravEngine>();

        private bool engaged = true;

        public CompProperties_DockingClamp Props => (CompProperties_DockingClamp)props;

        public bool Engaged => engaged;

        public bool PoweredOn
        {
            get
            {
                CompPowerTrader power = parent.GetComp<CompPowerTrader>();
                return power == null || power.PowerOn;
            }
        }

        /// <summary>夾具本身或四鄰的有效底板屬於的逆重引擎（沒有則 null）。不檢查夾持、電力與是否在船上。</summary>
        public Building_GravEngine FindGravship()
        {
            if (!parent.Spawned)
            {
                return null;
            }
            Map map = parent.Map;
            List<Building_GravEngine> engines = tmpEngines;
            engines.Clear();
            engines.AddRange(map.listerBuildings.AllBuildingsColonistOfClass<Building_GravEngine>());
            if (engines.Count == 0)
            {
                return null;
            }
            foreach (IntVec3 c in ContactCells())
            {
                if (!c.InBounds(map) || map.terrainGrid.FoundationAt(c) is not TerrainDef f || !f.IsSubstructure)
                {
                    continue;
                }
                for (int i = 0; i < engines.Count; i++)
                {
                    if (engines[i].Spawned && engines[i].ValidSubstructureAt(c))
                    {
                        return engines[i];
                    }
                }
            }
            return null;
        }

        private IEnumerable<IntVec3> ContactCells()
        {
            foreach (IntVec3 c in parent.OccupiedRect())
            {
                yield return c;
            }
            foreach (IntVec3 c in GenAdj.CellsAdjacentCardinal(parent))
            {
                yield return c;
            }
        }

        /// <summary>
        /// 由 <see cref="PileNetwork"/> 在重算 footprint 時呼叫：hull 是只由結構樁決定的船體範圍（不含停泊的底板，避免循環）。
        /// </summary>
        public DockingClampStatus StatusFor(HashSet<IntVec3> hull, out Building_GravEngine gravship)
        {
            gravship = null;
            if (!parent.Spawned || hull == null || !PileNetwork.FullyInside(parent, hull))
            {
                return DockingClampStatus.NotOnShip;
            }
            if (!engaged)
            {
                return DockingClampStatus.Released;
            }
            if (!PoweredOn)
            {
                return DockingClampStatus.Unpowered;
            }
            gravship = FindGravship();
            return gravship != null ? DockingClampStatus.Docked : DockingClampStatus.NoGravship;
        }

        /// <summary>目前狀態（找所屬網路的船體範圍）；給檢視字串與整備檢查用。</summary>
        public DockingClampStatus CurrentStatus(out Building_GravEngine gravship, out PileNetwork network)
        {
            gravship = null;
            network = null;
            MapComponent_PileNetwork comp = MapComponent_PileNetwork.For(parent.MapHeld);
            if (comp == null || !parent.Spawned)
            {
                return DockingClampStatus.NotOnShip;
            }
            List<PileNetwork> nets = comp.Networks;
            for (int i = 0; i < nets.Count; i++)
            {
                HashSet<IntVec3> hull = nets[i].HullFootprint;
                if (nets[i].covered.Contains(parent.Position) && PileNetwork.FullyInside(parent, hull))
                {
                    network = nets[i];
                    return StatusFor(hull, out gravship);
                }
            }
            return DockingClampStatus.NotOnShip;
        }

        private void SetEngaged(bool value)
        {
            if (engaged == value)
            {
                return;
            }
            engaged = value;
            MapComponent_PileNetwork.For(parent.MapHeld)?.NotifyDockingChanged();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            MapComponent_PileNetwork.For(parent.Map)?.RegisterClamp(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            MapComponent_PileNetwork.For(map)?.DeregisterClamp(this);
        }

        public override void ReceiveCompSignal(string signal)
        {
            base.ReceiveCompSignal(signal);
            if (signal == CompPowerTrader.PowerTurnedOnSignal || signal == CompPowerTrader.PowerTurnedOffSignal
                || signal == CompFlickable.FlickedOnSignal || signal == CompFlickable.FlickedOffSignal)
            {
                MapComponent_PileNetwork.For(parent.MapHeld)?.NotifyDockingChanged();
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
            if (parent.Faction != Faction.OfPlayer)
            {
                yield break;
            }
            Command_Toggle toggle = new Command_Toggle
            {
                defaultLabel = "FFF.Transfer.Clamp.Toggle".Translate(),
                defaultDesc = "FFF.Transfer.Clamp.ToggleDesc".Translate(),
                icon = parent.def.uiIcon,
                isActive = () => engaged,
                toggleAction = () => SetEngaged(!engaged)
            };
            CompTransferConsole launching = LaunchingConsole();
            if (launching != null)
            {
                toggle.Disable("FFF.Transfer.Clamp.LockedDuringLaunch".Translate());
            }
            yield return toggle;
        }

        /// <summary>同船正在起飛程序（倒數）中的控制台；倒數中不能放開或夾持。</summary>
        private CompTransferConsole LaunchingConsole()
        {
            CurrentStatus(out _, out PileNetwork net);
            if (net == null)
            {
                return null;
            }
            List<CompTransferConsole> consoles = MapComponent_PileNetwork.For(parent.Map).Consoles;
            for (int i = 0; i < consoles.Count; i++)
            {
                CompTransferConsole c = consoles[i];
                if (c.State == TransferConsoleState.Countdown && MapComponent_PileNetwork.For(parent.Map).NetworkContaining(c.parent) == net)
                {
                    return c;
                }
            }
            return null;
        }

        public override string CompInspectStringExtra()
        {
            if (!parent.Spawned)
            {
                return null;
            }
            DockingClampStatus status = CurrentStatus(out Building_GravEngine gravship, out _);
            switch (status)
            {
                case DockingClampStatus.Docked:
                    return "FFF.Transfer.Clamp.Status.Docked".Translate(gravship.RenamableLabel, gravship.ValidSubstructure.Count);
                case DockingClampStatus.Released:
                    return "FFF.Transfer.Clamp.Status.Released".Translate();
                case DockingClampStatus.Unpowered:
                    return "FFF.Transfer.Clamp.Status.Unpowered".Translate().Colorize(ColorLibrary.RedReadable);
                case DockingClampStatus.NoGravship:
                    return "FFF.Transfer.Clamp.Status.NoGravship".Translate().Colorize(ColorLibrary.RedReadable);
                default:
                    return TransferText.Key("FFF.Transfer.Clamp.Status.NotOnShip", parent).Translate().Colorize(ColorLibrary.RedReadable);
            }
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            if (CurrentStatus(out Building_GravEngine gravship, out _) == DockingClampStatus.Docked)
            {
                GenDraw.DrawFieldEdges(new List<IntVec3>(gravship.ValidSubstructure), Color.cyan);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref engaged, "engaged", true);
        }
    }
}
