using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Fortified
{
    public enum FacilityLockdownState
    {
        Idle,
        Countdown,
        Locked,
        Sealed,
        Disarmed,
        Lost
    }

    /// <summary>
    /// 地下設施的封鎖狀態。放在口袋地圖上，所以中控被摧毀後狀態仍然存在；地表入口與地下出口都查這裡。
    /// 流程：Idle →（警戒值滿）Countdown →（倒數結束）Locked →（駭入中控）Disarmed；
    /// 中控在任何階段被摧毀 → Sealed →（地表緊急解鎖）Disarmed；
    /// Locked / Sealed 時內外都沒人能解鎖 → 寬限一天 → Lost（困在裡面的玩家 pawn 失蹤）。
    /// 封鎖生效時地表入口的駭入進度歸零；Locked / Sealed 時重新駭開入口 → Disarmed（緊急解鎖完成則直接補滿入口駭入）。
    ///
    /// Lockdown state of an underground facility. Lives on the pocket map, so it outlasts the controller;
    /// both the surface entrance and the underground exit read it.
    /// Idle → (alert maxed) Countdown → (timer runs out) Locked → (controller hacked) Disarmed;
    /// controller destroyed at any point → Sealed → (surface override) Disarmed;
    /// Locked / Sealed with nobody able to unlock from either side → one-day grace → Lost.
    /// Once the lockdown bites, the surface entrance's hack is wiped; hacking it open again while Locked / Sealed
    /// → Disarmed (a finished override completes that hack outright).
    /// </summary>
    public class MapComponent_FacilityLockdown : MapComponent
    {
        public const int CountdownTicks = 3600;
        private const int CountdownMessageInterval = 600;
        private const int RescueCheckInterval = 250;
        private const int GraceTicks = GenDate.TicksPerDay;

        private FacilityLockdownState state = FacilityLockdownState.Idle;
        private Thing controller;
        private int countdownLeft = -1;
        private int graceLeft = -1;

        /// <summary>
        /// 這次封鎖是否已把地表入口的駭入歸零。一張口袋地圖最多封鎖一次（Disarmed / Lost 都是終點），一個旗標就夠；
        /// 放在 tick 而不是狀態轉換點，存檔裡早就封鎖的設施也會補上。
        /// Whether this lockdown has already wiped the entrance hack. A pocket map locks down at most once
        /// (Disarmed and Lost are terminal), so one flag does; checking from the tick also fixes saves locked before this.
        /// </summary>
        private bool entranceHackReset;

        public MapComponent_FacilityLockdown(Map map) : base(map) { }

        public FacilityLockdownState State => state;

        /// <summary>出入口此刻是否被封住。Whether the lifts are blocked right now.</summary>
        public bool BlocksPortals => state == FacilityLockdownState.Locked
                                  || state == FacilityLockdownState.Sealed
                                  || state == FacilityLockdownState.Lost;

        public int CountdownLeft => countdownLeft;
        public int GraceLeft => graceLeft;
        public Thing Controller => controller;

        /// <summary>通往這張口袋地圖的地表入口。The surface portal leading into this pocket map.</summary>
        public MapPortal SurfacePortal => FacilityLockdownUtility.FindSurfacePortal(map);

        public static MapComponent_FacilityLockdown For(Map pocketMap)
        {
            return pocketMap?.GetComponent<MapComponent_FacilityLockdown>();
        }

        // ── 由中控呼叫 / Called by the controller ─────────────────────────

        public void Register(Thing c)
        {
            controller = c;
        }

        public void Notify_AlertFull()
        {
            if (state != FacilityLockdownState.Idle) return;
            state = FacilityLockdownState.Countdown;
            countdownLeft = CountdownTicks;
            Find.LetterStack.ReceiveLetter(
                "FFF_Lockdown_StartedLabel".Translate(),
                "FFF_Lockdown_StartedText".Translate(CountdownTicks.ToStringTicksToPeriod()),
                LetterDefOf.ThreatBig, controller);
        }

        public void Notify_ControllerHacked(Pawn hacker)
        {
            if (state == FacilityLockdownState.Lost || state == FacilityLockdownState.Disarmed) return;
            bool wasActive = state != FacilityLockdownState.Idle;
            state = FacilityLockdownState.Disarmed;
            countdownLeft = -1;
            graceLeft = -1;
            string key = wasActive ? "FFF_Lockdown_Lifted" : "FFF_Lockdown_Disarmed";
            Messages.Message(key.Translate(hacker.Named("HACKER")), controller, MessageTypeDefOf.PositiveEvent);
        }

        public void Notify_ControllerDestroyed()
        {
            controller = null;
            if (state == FacilityLockdownState.Disarmed || state == FacilityLockdownState.Lost) return;
            state = FacilityLockdownState.Sealed;
            countdownLeft = -1;
            Find.LetterStack.ReceiveLetter(
                "FFF_Lockdown_SealedLabel".Translate(),
                "FFF_Lockdown_SealedText".Translate(),
                LetterDefOf.ThreatBig, SurfacePortal);
        }

        // ── 由地表入口呼叫 / Called by the surface entrance ───────────────

        /// <summary>
        /// 從地表解除封鎖：緊急解鎖完成，或重新駭開入口。前者會順便補滿入口的駭入；後者由入口自己的 CompHackable 觸發，
        /// 補滿時再呼叫回來也只會因為已不再封鎖而直接返回。
        /// Lifted from the surface: the emergency override finished, or the entrance was hacked open again. The
        /// override also completes the entrance hack; that re-enters here and returns at once, since nothing blocks any more.
        /// </summary>
        public void Notify_SurfaceOverride(Pawn pawn)
        {
            if (!BlocksPortals || state == FacilityLockdownState.Lost) return;
            state = FacilityLockdownState.Disarmed;
            graceLeft = -1;
            MapPortal portal = SurfacePortal;
            FacilityLockdownUtility.RestoreEntranceHack(portal);
            // 開發者工具直接完成駭入時沒有 pawn。Dev "complete hack" has no pawn.
            if (pawn != null)
            {
                Messages.Message("FFF_Lockdown_Overridden".Translate(pawn.Named("PAWN")), portal, MessageTypeDefOf.PositiveEvent);
            }
        }

        public void Notify_Lost()
        {
            state = FacilityLockdownState.Lost;
            countdownLeft = -1;
            graceLeft = -1;
        }

        // ── Tick ──────────────────────────────────────────────────────────

        public override void MapComponentTick()
        {
            if (BlocksPortals && !entranceHackReset)
            {
                entranceHackReset = true;
                FacilityLockdownUtility.ResetEntranceHack(SurfacePortal);
            }

            switch (state)
            {
                case FacilityLockdownState.Countdown:
                    TickCountdown();
                    break;
                case FacilityLockdownState.Locked:
                case FacilityLockdownState.Sealed:
                    if (Find.TickManager.TicksGame % RescueCheckInterval != 0) break;
                    // 入口已被重新駭開卻還在封鎖（例如駭入早於這段邏輯的存檔），補上解除。
                    // The entrance is hacked yet still locked (e.g. a save hacked before this existed): lift it now.
                    MapPortal portal = SurfacePortal;
                    if (portal?.TryGetComp<CompHackable>()?.IsHacked == true)
                    {
                        Notify_SurfaceOverride(null);
                        break;
                    }
                    TickRescue();
                    break;
            }
        }

        private void TickCountdown()
        {
            // 中控被 EMP 癱瘓時倒數暫停。Countdown pauses while the controller is stunned.
            CompStunnable stun = controller?.TryGetComp<CompStunnable>();
            if (stun?.StunHandler != null && stun.StunHandler.Stunned) return;

            if (countdownLeft > 0 && countdownLeft % CountdownMessageInterval == 0)
            {
                Messages.Message("FFF_Lockdown_Countdown".Translate(countdownLeft.ToStringTicksToPeriod()),
                    controller, MessageTypeDefOf.ThreatBig, historical: false);
            }
            if (--countdownLeft > 0) return;

            state = FacilityLockdownState.Locked;
            countdownLeft = -1;
            Find.LetterStack.ReceiveLetter(
                "FFF_Lockdown_LockedLabel".Translate(),
                "FFF_Lockdown_LockedText".Translate(),
                LetterDefOf.ThreatBig, controller);
        }

        private void TickRescue()
        {
            // 裡面沒人就不算受困，門只是鎖著。Nobody inside: nobody is trapped, the door is just locked.
            if (!AnyPlayerPawnInside())
            {
                graceLeft = -1;
                return;
            }

            if (CanRescueFromInside() || CanRescueFromOutside())
            {
                if (graceLeft >= 0)
                {
                    graceLeft = -1;
                    Messages.Message("FFF_Lockdown_GraceCancelled".Translate(), MessageTypeDefOf.PositiveEvent);
                }
                return;
            }

            if (graceLeft < 0)
            {
                graceLeft = GraceTicks;
                Find.LetterStack.ReceiveLetter(
                    "FFF_Lockdown_TrappedLabel".Translate(),
                    "FFF_Lockdown_TrappedText".Translate(GraceTicks.ToStringTicksToPeriod()),
                    LetterDefOf.ThreatBig, SurfacePortal);
                return;
            }

            graceLeft -= RescueCheckInterval;
            if (graceLeft <= 0) FacilityLockdownUtility.DeclareLost(map, this);
        }

        // ── 判定 / Checks ─────────────────────────────────────────────────

        private bool AnyPlayerPawnInside()
        {
            return map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).Any();
        }

        private bool CanRescueFromInside()
        {
            if (state != FacilityLockdownState.Locked) return false;
            if (controller == null || controller.Destroyed) return false;
            CompHackable hack = controller.TryGetComp<CompHackable>();
            if (hack == null || hack.IsHacked || hack.LockedOut) return false;
            return map.mapPawns.FreeColonistsSpawned.Any(p => !p.Downed && hack.CanHackNow(p).Accepted);
        }

        /// <summary>地表有人能緊急解鎖（只有 Sealed），或能重新駭開入口。Someone on the surface can override (Sealed only) or re-hack the entrance.</summary>
        private bool CanRescueFromOutside()
        {
            MapPortal portal = SurfacePortal;
            if (portal == null || !portal.Spawned) return false;
            CompFacilityLockdownGate gate = portal.TryGetComp<CompFacilityLockdownGate>();
            if (gate == null) return false;
            CompHackable hack = portal.TryGetComp<CompHackable>();
            bool hackable = hack != null && !hack.IsHacked && !hack.LockedOut;
            return portal.Map.mapPawns.FreeColonistsSpawned.Any(p =>
                gate.CanOverride(p).Accepted || (hackable && !p.Downed && hack.CanHackNow(p).Accepted));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref state, "fff_lockdownState", FacilityLockdownState.Idle);
            Scribe_References.Look(ref controller, "fff_lockdownController");
            Scribe_Values.Look(ref countdownLeft, "fff_lockdownCountdown", -1);
            Scribe_Values.Look(ref graceLeft, "fff_lockdownGrace", -1);
            Scribe_Values.Look(ref entranceHackReset, "fff_lockdownEntranceHackReset", false);
        }
    }
}
