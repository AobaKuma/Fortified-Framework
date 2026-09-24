using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Fortified
{
    public class CompProperties_FacilityLockdownGate : CompProperties
    {
        /// <summary>地表緊急解鎖的工作量，以 HackingSpeed 推進。Surface override work, driven by HackingSpeed.</summary>
        public float overrideWork = 6000f;

        /// <summary>執行緊急解鎖所需的智識等級。Intellectual level needed for the override.</summary>
        public int overrideSkillPrerequisite = 6;

        public CompProperties_FacilityLockdownGate()
        {
            compClass = typeof(CompFacilityLockdownGate);
        }
    }

    /// <summary>
    /// 設施封鎖的出入口閘門，掛在地表入口（MapPortal）與地下出口（PocketMapExit）上。
    /// 原版 <see cref="MapPortal.IsEnterable"/> 會逐一詢問 comp 的 <see cref="ThingComp.CanEnterPortal"/>，
    /// 所以不需要 Harmony：封鎖時兩端都拒絕進出，正在前往的工作也會因 JobDriver_EnterPortal 的 FailOn 中止。
    /// 在地表那一端、且中控已毀（Sealed）時，提供「緊急解鎖」。
    ///
    /// The lockdown gate, on both the surface entrance (MapPortal) and the underground exit (PocketMapExit).
    /// Vanilla <see cref="MapPortal.IsEnterable"/> asks every comp's <see cref="ThingComp.CanEnterPortal"/>,
    /// so no Harmony is needed: both ends refuse while locked, and enter jobs already under way fail through
    /// JobDriver_EnterPortal's FailOn. On the surface end, once the controller is gone (Sealed), it offers
    /// an emergency override.
    /// </summary>
    public class CompFacilityLockdownGate : ThingComp
    {
        private float overrideProgress;

        /// <summary>
        /// 失蹤判定後口袋地圖已被移除、狀態也跟著消失，改由地表端自己記住「永久封死」，否則原版會重新產生一張新地圖。
        /// After a Lost verdict the pocket map (and its state) is gone; the surface end remembers the seal itself,
        /// or vanilla would happily generate a fresh map.
        /// </summary>
        private bool permanentlySealed;

        public CompProperties_FacilityLockdownGate Props => (CompProperties_FacilityLockdownGate)props;

        private MapPortal Portal => parent as MapPortal;

        private bool IsSurfaceEnd => !(parent is PocketMapExit);

        public bool PermanentlySealed => permanentlySealed;

        public float OverrideProgressPct => Props.overrideWork <= 0f ? 1f : overrideProgress / Props.overrideWork;

        /// <summary>地表端讀它通往的口袋地圖；地下端讀自己所在的地圖。Surface end reads its pocket map; the exit reads its own map.</summary>
        public MapComponent_FacilityLockdown Lockdown
        {
            get
            {
                if (IsSurfaceEnd)
                {
                    MapPortal portal = Portal;
                    return portal != null && portal.PocketMapExists ? MapComponent_FacilityLockdown.For(portal.PocketMap) : null;
                }
                return MapComponent_FacilityLockdown.For(parent.MapHeld);
            }
        }

        public void Notify_PermanentlySealed()
        {
            permanentlySealed = true;
        }

        public override AcceptanceReport CanEnterPortal()
        {
            if (permanentlySealed) return "FFF_Lockdown_GateLost".Translate();
            MapComponent_FacilityLockdown ld = Lockdown;
            if (ld == null || !ld.BlocksPortals) return true;
            return ld.State == FacilityLockdownState.Sealed
                ? "FFF_Lockdown_GateSealed".Translate()
                : "FFF_Lockdown_GateLocked".Translate();
        }

        // ── 緊急解鎖 / Emergency override ─────────────────────────────────

        public bool OverrideAvailable => IsSurfaceEnd && !permanentlySealed && Lockdown?.State == FacilityLockdownState.Sealed;

        public AcceptanceReport CanOverride(Pawn pawn)
        {
            if (!OverrideAvailable) return false;
            if (pawn.Downed || !HackUtility.IsCapableOfHacking(pawn)) return "IncapableOfHacking".Translate();
            SkillRecord skill = pawn.skills?.GetSkill(SkillDefOf.Intellectual);
            if (skill == null || skill.Level < Props.overrideSkillPrerequisite)
            {
                return "SkillTooLow".Translate(SkillDefOf.Intellectual.label, skill?.Level ?? 0, Props.overrideSkillPrerequisite);
            }
            if (!pawn.CanReach(parent, PathEndMode.Touch, Danger.Deadly)) return "NoPath".Translate().CapitalizeFirst();
            return true;
        }

        public void DoOverrideWork(Pawn pawn, float amount)
        {
            if (!OverrideAvailable) return;
            overrideProgress += amount;
            if (overrideProgress >= Props.overrideWork)
            {
                overrideProgress = 0f;
                Lockdown?.Notify_SurfaceOverride(pawn);
            }
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            if (!OverrideAvailable) yield break;
            AcceptanceReport report = CanOverride(selPawn);
            if (!report.Accepted)
            {
                yield return new FloatMenuOption("FFF_Lockdown_CannotOverride".Translate() + ": " + report.Reason, null);
                yield break;
            }
            yield return new FloatMenuOption("FFF_Lockdown_Override".Translate(parent.Label), delegate
            {
                selPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(FFF_JobDefOf.FFF_LockdownOverride, parent), JobTag.Misc);
            });
        }

        public override string CompInspectStringExtra()
        {
            if (permanentlySealed) return "FFF_Lockdown_GateLost".Translate();
            MapComponent_FacilityLockdown ld = Lockdown;
            if (ld == null) return null;
            switch (ld.State)
            {
                case FacilityLockdownState.Countdown:
                    return ((string)"FFF_Lockdown_InspectCountdown".Translate(ld.CountdownLeft.ToStringTicksToPeriod())).Colorize(ColorLibrary.RedReadable);
                case FacilityLockdownState.Locked:
                    string locked = ((string)"FFF_Lockdown_GateLocked".Translate()).Colorize(ColorLibrary.RedReadable);
                    if (ld.GraceLeft > 0)
                    {
                        locked += "\n" + "FFF_Lockdown_GraceLeft".Translate(ld.GraceLeft.ToStringTicksToPeriod());
                    }
                    return locked;
                case FacilityLockdownState.Sealed:
                    string s = ((string)"FFF_Lockdown_GateSealed".Translate()).Colorize(ColorLibrary.RedReadable);
                    if (IsSurfaceEnd && overrideProgress > 0f)
                    {
                        s += "\n" + "FFF_Lockdown_OverrideProgress".Translate(OverrideProgressPct.ToStringPercent());
                    }
                    if (ld.GraceLeft > 0)
                    {
                        s += "\n" + "FFF_Lockdown_GraceLeft".Translate(ld.GraceLeft.ToStringTicksToPeriod());
                    }
                    return s;
                case FacilityLockdownState.Lost:
                    return "FFF_Lockdown_GateLost".Translate();
                default:
                    return null;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref overrideProgress, "fff_overrideProgress", 0f);
            Scribe_Values.Look(ref permanentlySealed, "fff_permanentlySealed", false);
        }
    }
}
