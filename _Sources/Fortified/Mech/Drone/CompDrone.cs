using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Runtime.Remoting.Messaging;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Noise;
using Multiplayer.API;

namespace Fortified
{
    /// <summary>
    /// Drone 是一次性使用的遙控機器人。
    /// </summary>
    public class CompDrone : ThingComp
    {
        private Pawn Pawn => parent as Pawn;
        protected Thing parentPlatform = null;
        public Thing Platform => parentPlatform;//召喚的Thing。
        private CompMechPowerCell powerCell;
        public Thing PlatformOwner
        {
            get
            {
                if (parentPlatform == null) return null;
                if (isApparelPlatform) return Apparel.Wearer;
                return parentPlatform;
            }
        }

        private bool isApparelPlatform = false;
        public bool IsApparelPlatform => isApparelPlatform;
        public Apparel Apparel => Platform as Apparel;

        public CompProperties_Drone Props => (CompProperties_Drone)this.props;

        public AcceptanceReport CanDraft
        {
            get
            {
                if (parentPlatform == null) return new AcceptanceReport("FFF.Drone.NoControlPlatform".Translate());

                if (isApparelPlatform)
                {
                    Pawn wearer = Apparel.Wearer;
                    return CanDraftAsPawnPlatform(wearer);
                }
                else if (parentPlatform is Pawn p)
                {
                    return CanDraftAsPawnPlatform(p);
                }
                return CanDraftAsBuildingPlatform();
            }
        }
        public override void PostPostMake()
        {
            //玩家召喚的並不會有自帶裝備。
            if (this.parent.Faction == Faction.OfPlayer)
            {
                Pawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
            }
        }
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerCell = parent.TryGetComp<CompMechPowerCell>();
            if (!respawningAfterLoad && parent.Faction != Faction.OfPlayer)
            {
                if (this.parent.TryGetComp<CompExplosiveOnMelee>(out var e) && Rand.Chance(0.5f))
                {
                    Thing shell = ThingMaker.MakeThing(ThingDefOf.Shell_HighExplosive);
                    Pawn.inventory ??= new Pawn_InventoryTracker(Pawn);
                    Pawn.inventory.innerContainer.TryAdd(shell, 1);
                }
            }
        }
        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)
        {
            if (this.parent.Faction == Faction.OfPlayer)
            {
                (this.parent as Pawn).equipment?.DropAllEquipment(parent.Position);
            }
            base.Notify_Killed(prevMap, dinfo);
        }
        protected virtual AcceptanceReport CanDraftAsPawnPlatform(Pawn p)
        {
            if (p == null) return new AcceptanceReport("FFF.Drone.NoController".Translate());
            if (!p.Drafted) return new AcceptanceReport("FFF.Drone.ControllerNotDrafted".Translate());
            if (!p.Spawned) return new AcceptanceReport("FFF.Drone.ControllerNotInMap".Translate());
            if (p.Map != parent.Map) return new AcceptanceReport("FFF.Drone.ControllerNotInMap".Translate());
            if (!p.IsPlayerControlled) return new AcceptanceReport("FFF.Drone.ControllerNotInControl".Translate());
            return true;
        }
        protected virtual AcceptanceReport CanDraftAsBuildingPlatform()
        {
            if (parentPlatform == null) return new AcceptanceReport("FFF.Drone.NoController".Translate());
            if (!parentPlatform.Spawned) return new AcceptanceReport("FFF.Drone.ControllerNotInMap".Translate());
            if (parentPlatform.Faction != Faction.OfPlayer) return new AcceptanceReport("FFF.Drone.ControllerNotInControl".Translate());
            if (parentPlatform.TryGetComp<CompPowerTrader>(out var _t) && !_t.PowerOn) return new AcceptanceReport("FFF.Drone.ControllerNotInControl".Translate());
            if (parentPlatform.TryGetComp<CompBreakdownable>(out var _p) && _p.BrokenDown) return new AcceptanceReport("FFF.Drone.ControllerNotInControl".Translate());
            if (parentPlatform.TryGetComp<CompFlickable>(out var _f) && _f.SwitchIsOn == false) return new AcceptanceReport("FFF.Drone.ControllerNotInControl".Translate());
            return true;
        }

        public bool HasPlatform
        {
            get
            {
                if (parentPlatform == null) return false;
                if (isApparelPlatform)
                {
                    return Apparel.Wearer != null && Apparel.Wearer.Spawned && Apparel.Wearer.Map == parent.Map;
                }
                return parentPlatform.Spawned && parentPlatform.Map == parent.Map;
            }
        }
        public void SetPlatform(Thing thing)
        {
            parentPlatform = thing;
            if (thing.def.IsApparel) isApparelPlatform = true;
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            if (parent is Pawn p && p.Faction == Faction.OfPlayer && Props.returnToDraftPlatformJob != null && HasPlatform)
            {
                var draftGizmo = new Command_Action
                {
                    defaultLabel = "FFF.Drone.Return".Translate(),
                    defaultDesc = "FFF.Drone.ReturnDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get(Props.returnGizmoPath),
                    action = () =>
                    {
                        ReturnToPlatform();
                    }
                };
                yield return draftGizmo;
            }
        }
        public override void CompTick()
        {
            if (!parent.Spawned || (parent as Pawn).DeadOrDowned) return;

            if (!parent.IsHashIntervalTick(500)) return;
            if (!CanDraft && Pawn.drafter != null) Pawn.drafter.Drafted = false;

            if (Pawn.CurJobDef != Props.returnToDraftPlatformJob && powerCell != null && powerCell.PowerTicksLeft < 5000)
            {
                ReturnToPlatform(forceInterrupt: true);
            }
        }
        bool noPlatformWarning = false;
        [SyncMethod]
        public void ReturnToPlatform(bool forceInterrupt = false)
        {
            if (!HasPlatform && !noPlatformWarning)
            {
                //如果沒有平台則警告 (Translation: Warning if no platform is available)
                Messages.Message("FFF.Drone.NoPlatform".Translate(parent.Label), MessageTypeDefOf.RejectInput, false);
                noPlatformWarning = true;
                return;
            }
            if (!HasPlatform) return;

            // 低电量时尝试强制中断当前工作，防止机械体沉迷工作忘记充电 (Translation?: Forces to stop task when battery low)
            if (forceInterrupt)
            {
                Pawn.jobs.StopAll();
               
            }

            if (isApparelPlatform)
            {
                Pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(Props.returnToDraftPlatformJob, PlatformOwner, Apparel), JobTag.DraftedOrder);

            }
            else
            {
                Pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(Props.returnToDraftPlatformJob, PlatformOwner), JobTag.DraftedOrder);

            }
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref parentPlatform, "parentPlatform");
            Scribe_Values.Look(ref isApparelPlatform, "isApparelPlatform", false);
        }
    }
    public class CompProperties_Drone : CompProperties
    {
        [NoTranslate]
        public string returnGizmoPath = "UI/Drone_Retract";

        public JobDef returnToDraftPlatformJob = null;
        public CompProperties_Drone()
        {
            this.compClass = typeof(CompDrone);
        }
    }

    [HarmonyPatch(typeof(PawnComponentsUtility), nameof(PawnComponentsUtility.AddAndRemoveDynamicComponents))]
    internal static class Patch_AddAndRemoveDynamicComponents
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, bool actAsIfSpawned)
        {
            if (!ModsConfig.BiotechActive || pawn.kindDef == null) return;

            // 处理 CompDrone、WeaponUsableMech 和 HumanlikeMech 的 workSettings 初始化
            bool needsWorkSettings = pawn.TryGetComp<CompDrone>() != null
                || pawn is WeaponUsableMech
                || pawn is IHumanlikeMech;

            if (needsWorkSettings && pawn.workSettings == null)
            {
                pawn.workSettings = new Pawn_WorkSettings(pawn);
                pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                // 限制機械體工作
                if (!pawn.RaceProps.IsMechanoid && !pawn.RaceProps.mechEnabledWorkTypes.NullOrEmpty())
                {
                    foreach (WorkTypeDef w in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                    {
                        if (!pawn.RaceProps.mechEnabledWorkTypes.Contains(w))
                        {
                            pawn.workSettings.SetPriority(w, 0);
                        }
                    }
                }
            }
        }
    }

    //[HarmonyPatch(typeof(ThinkNode_ConditionalWorkMode), "Satisfied")]
    //internal static class Patch_Satisfied
    //{
    //    [HarmonyPostfix]
    //    public static void Postfix(Pawn pawn, ref bool __result)
    //    {
    //        if (__result) return;
    //        if (pawn.Faction == Faction.OfPlayer && pawn.TryGetComp<CompDrone>() != null)
    //        {
    //            __result = true;
    //        }
    //    }
    //}

    [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.ShowDraftGizmo), MethodType.Getter)]
    internal static class Patch_ShowDraftGizmo
    {
        internal static void Postfix(Pawn_DraftController __instance, ref bool __result)
        {
            if (__result) return;
            if (__instance.pawn.Faction == Faction.OfPlayer && __instance.pawn.TryGetComp<CompDrone>() != null) __result = true;
        }
    }
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.CanDraftMech))]
    internal static class Patch_CanDraftMech
    {
        internal static void Postfix(Pawn mech, ref AcceptanceReport __result)
        {
            if (__result) return;
            if (mech.TryGetComp<CompDrone>(out var d)) __result = d.CanDraft;
        }
    }
}
