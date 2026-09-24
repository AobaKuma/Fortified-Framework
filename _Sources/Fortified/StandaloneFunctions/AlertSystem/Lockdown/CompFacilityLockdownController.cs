using RimWorld;
using Verse;

namespace Fortified
{
    public class CompProperties_FacilityLockdownController : CompProperties
    {
        /// <summary>多久檢查一次地圖警戒值。How often to poll the map's alert level.</summary>
        public int checkInterval = 60;

        /// <summary>倒數中的特效。Effecter while counting down.</summary>
        public EffecterDef countdownEffecter;

        /// <summary>封鎖中的特效。Effecter while locked.</summary>
        public EffecterDef lockedEffecter;

        public CompProperties_FacilityLockdownController()
        {
            compClass = typeof(CompFacilityLockdownController);
        }
    }

    /// <summary>
    /// 設施封鎖中控：盯著地圖警戒值，滿 100% 就通知 <see cref="MapComponent_FacilityLockdown"/> 開始倒數；
    /// 被駭入（同一建築上的原版 CompHackable）就解除，被摧毀就讓設施死鎖。
    /// 本身不需要電力，也不受休眠影響；EMP 只會讓倒數暫停（由 MapComponent 判斷）。
    ///
    /// Lockdown controller: watches the map's alert level and starts the countdown at 100%. Hacking it
    /// (vanilla CompHackable on the same building) disarms the lockdown; destroying it seals the facility.
    /// Needs no power and never goes dormant; an EMP only pauses the countdown (checked by the MapComponent).
    /// </summary>
    public class CompFacilityLockdownController : ThingComp
    {
        [Unsaved(false)]
        private Effecter effecter;

        [Unsaved(false)]
        private EffecterDef effecterDef;

        public CompProperties_FacilityLockdownController Props => (CompProperties_FacilityLockdownController)props;

        private MapComponent_FacilityLockdown Lockdown => MapComponent_FacilityLockdown.For(parent.MapHeld);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            Lockdown?.Register(parent);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned) return;
            MapComponent_FacilityLockdown ld = Lockdown;
            if (ld == null) return;

            if (ld.State == FacilityLockdownState.Idle && parent.IsHashIntervalTick(Props.checkInterval))
            {
                MapComponent_AlertCounter counter = parent.Map.GetComponent<MapComponent_AlertCounter>();
                if (counter != null && (counter.IsTriggered || counter.AlertLevelPct >= 1f))
                {
                    ld.Notify_AlertFull();
                }
            }

            UpdateEffecter(ld.State);
        }

        public override void Notify_Hacked(Pawn hacker)
        {
            base.Notify_Hacked(hacker);
            Lockdown?.Notify_ControllerHacked(hacker);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            ClearEffecter();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            ClearEffecter();
            // 整張口袋地圖被移除、或被替換時不算「摧毀」。
            // The whole pocket map being torn down, or a replacement, doesn't count as destruction.
            if (mode == DestroyMode.Vanish || mode == DestroyMode.WillReplace) return;
            MapComponent_FacilityLockdown.For(previousMap)?.Notify_ControllerDestroyed();
        }

        private void UpdateEffecter(FacilityLockdownState state)
        {
            EffecterDef want = state == FacilityLockdownState.Countdown ? Props.countdownEffecter
                             : state == FacilityLockdownState.Locked ? Props.lockedEffecter
                             : null;
            if (want == null)
            {
                ClearEffecter();
                return;
            }
            if (effecter == null || effecterDef != want)
            {
                ClearEffecter();
                effecter = want.Spawn(parent, parent.Map);
                effecterDef = want;
            }
            effecter.EffectTick(parent, TargetInfo.Invalid);
        }

        private void ClearEffecter()
        {
            effecter?.Cleanup();
            effecter = null;
            effecterDef = null;
        }

        public override string CompInspectStringExtra()
        {
            MapComponent_FacilityLockdown ld = Lockdown;
            if (ld == null) return null;
            switch (ld.State)
            {
                case FacilityLockdownState.Countdown:
                    return ((string)"FFF_Lockdown_InspectCountdown".Translate(ld.CountdownLeft.ToStringTicksToPeriod())).Colorize(ColorLibrary.RedReadable);
                case FacilityLockdownState.Locked:
                    return ((string)"FFF_Lockdown_InspectLocked".Translate()).Colorize(ColorLibrary.RedReadable);
                case FacilityLockdownState.Disarmed:
                    return "FFF_Lockdown_InspectDisarmed".Translate();
                default:
                    return "FFF_Lockdown_InspectIdle".Translate();
            }
        }
    }
}
