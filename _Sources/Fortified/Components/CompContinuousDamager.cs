using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Fortified
{
    /// <summary>
    /// When activated, continuously deals high damage to every Thing in the cell directly
    /// in front of the parent.  After <see cref="CompProperties_ContinuousDamager.durationTicks"/>
    /// the parent destroys itself.
    /// </summary>
    public class CompContinuousDamager : ThingComp
    {
        // ── state ─────────────────────────────────────────────────────────────
        private bool active;
        private int  ticksRemaining;
        private int  ticksSinceLastHit;

        // ── runtime (not saved) ───────────────────────────────────────────────
        private Effecter _activeEffecter;
        private Effecter _hitEffecter;
        private IntVec3  _hitEffecterCell = IntVec3.Invalid;

        // ── props shortcut / public state ────────────────────────────────────
        public CompProperties_ContinuousDamager Props =>
            (CompProperties_ContinuousDamager)props;

        public bool Active => active;

        // ── save / load ───────────────────────────────────────────────────────
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref active,         nameof(active),         false);
            Scribe_Values.Look(ref ticksRemaining, nameof(ticksRemaining), 0);
            Scribe_Values.Look(ref ticksSinceLastHit, nameof(ticksSinceLastHit), 0);
        }

        // ── respawn effecter after load ───────────────────────────────────────
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (active && respawningAfterLoad)
                SpawnActiveEffecter();
        }

        // ── tick ──────────────────────────────────────────────────────────────
        public override void CompTickInterval(int delta)
        {
            if (!active || !parent.Spawned) return;

            // Tick both persistent effecters every interval.
            TickActiveEffecter();
            TickHitEffecter();

            ticksRemaining -= delta;

            if (ticksRemaining <= 0)
            {
                // Time's up: deal one last hit, then destroy.
                DealDamageToFront();
                CleanupActiveEffecter();
                parent.Map.listerThings.Remove(parent); // ensure clean removal
                parent.Destroy(DestroyMode.KillFinalize);
                return;
            }

            ticksSinceLastHit += delta;
            if (ticksSinceLastHit >= Props.tickInterval)
            {
                ticksSinceLastHit = 0;
                DealDamageToFront();
            }
        }

        // ── activation ────────────────────────────────────────────────────────
        /// <summary>Activate the comp. Safe to call multiple times; re-activation resets the timer.</summary>
        public void Activate()
        {
            active            = true;
            ticksRemaining    = Props.durationTicks;
            ticksSinceLastHit = 0;
            Props.activateSound?.PlayOneShot(SoundInfo.InMap(new TargetInfo(parent.Position, parent.Map)));
            SpawnActiveEffecter();
        }

        // ── damage logic ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns true for Things that constitute a physical obstruction worth
        /// dealing damage to.  Filth, Gas, and non-solid Items on the floor are
        /// excluded so the beam skips over them and keeps scanning.
        /// </summary>
        private static bool IsValidTarget(Thing t)
        {
            if (t.Destroyed)                              return false;
            if (t.def == null)                            return false;
            var cat = t.def.category;
            // Accept pawns and buildings only; everything else is floor clutter.
            return cat == ThingCategory.Pawn || cat == ThingCategory.Building;
        }

        private void DealDamageToFront()
        {
            if (!parent.Spawned || parent.Destroyed) return;

            IntVec3 dir = parent.Rotation.FacingCell;
            Map     map = parent.Map;

            // Scan forward up to Props.range cells.
            // Skip cells that contain no valid targets; stop at the first cell
            // that DOES contain at least one valid target.
            for (int i = 1; i <= Props.range; i++)
            {
                IntVec3 cell = parent.Position + dir * i;
                if (!cell.InBounds(map)) break;

                // Copy list: TakeDamage may mutate the grid during iteration.
                List<Thing> things = new List<Thing>(cell.GetThingList(map));

                // First pass: check whether this cell has any valid target.
                bool hasValidTarget = false;
                foreach (Thing t in things)
                {
                    if (t != parent && IsValidTarget(t)) { hasValidTarget = true; break; }
                }
                if (!hasValidTarget) continue; // no valid target here → keep scanning

                // Second pass: apply damage to every valid target in this cell.
                foreach (Thing target in things)
                {
                    if (target == parent || !IsValidTarget(target)) continue;

                    var dinfo = new DamageInfo(
                        def:              Props.damageDef,
                        amount:           Props.damageAmount,
                        armorPenetration: Props.armorPenetration,
                        instigator:       parent
                    );
                    target.TakeDamage(dinfo);
                }

                // Update the persistent hit effecter to this cell.
                UpdateHitEffecter(cell, map);
                return; // stop after the first valid-target cell
            }

            // Nothing in range: let the hit effecter fade out.
            CleanupHitEffecter();
        }

        // ── gizmo ─────────────────────────────────────────────────────────────
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!Props.showGizmo) yield break;

            yield return new Command_Action
            {
                defaultLabel = active
                    ? "FFF_CompContinuousDamager_Deactivate".Translate()
                    : "FFF_CompContinuousDamager_Activate".Translate(),
                defaultDesc = "FFF_CompContinuousDamager_Desc".Translate(
                    Props.durationTicks.ToStringTicksToPeriod(),
                    Props.damageAmount,
                    Props.damageDef.label),
                icon   = ContentFinder<Texture2D>.Get(Props.gizmoIcon, false)
                      ?? BaseContent.BadTex,
                action = () =>
                {
                    if (!active) Activate();
                    else         Deactivate();
                }
            };
        }

        public void Deactivate()
        {
            active            = false;
            ticksRemaining    = 0;
            ticksSinceLastHit = 0;
            CleanupActiveEffecter();
            CleanupHitEffecter();
        }

        // ── effecter helpers ─────────────────────────────────────────────────
        private void SpawnActiveEffecter()
        {
            if (Props.activeEffecter == null || !parent.Spawned) return;
            CleanupActiveEffecter(); // never double-spawn
            _activeEffecter        = Props.activeEffecter.SpawnAttached(parent, parent.Map);
            // Shift the spray origin to the front edge of the building.
            _activeEffecter.offset = ActiveEffecterWorldOffset();
        }

        /// <summary>
        /// Computes the world-space offset that pushes the active effecter origin
        /// to the front edge of the parent, accounting for rotation.
        /// </summary>
        private Vector3 ActiveEffecterWorldOffset()
        {
            IntVec3 facingDir = parent.Rotation.FacingCell;
            return new Vector3(facingDir.x, 0f, facingDir.z) * Props.activeEffecterForwardOffset;
        }

        private void TickActiveEffecter()
        {
            if (_activeEffecter == null) return;

            // Aim toward the first cell in range that holds a valid target;
            // fall back to the immediate front cell if nothing is found.
            IntVec3 aimCell  = FindFirstValidTargetCell();
            Map     map      = parent.Map;
            TargetInfo aimTI = aimCell.IsValid && aimCell.InBounds(map)
                ? new TargetInfo(aimCell, map)
                : new TargetInfo(parent.Position + parent.Rotation.FacingCell, map);

            _activeEffecter.EffectTick(new TargetInfo(parent), aimTI);
        }

        /// <summary>
        /// Scans forward and returns the first cell that contains a valid target,
        /// or IntVec3.Invalid if none is found within range.
        /// </summary>
        private IntVec3 FindFirstValidTargetCell()
        {
            if (!parent.Spawned) return IntVec3.Invalid;
            IntVec3 dir = parent.Rotation.FacingCell;
            Map     map = parent.Map;
            for (int i = 1; i <= Props.range; i++)
            {
                IntVec3 cell = parent.Position + dir * i;
                if (!cell.InBounds(map)) break;
                foreach (Thing t in cell.GetThingList(map))
                    if (t != parent && IsValidTarget(t)) return cell;
            }
            return IntVec3.Invalid;
        }

        private void CleanupActiveEffecter()
        {
            _activeEffecter?.Cleanup();
            _activeEffecter = null;
        }

        // ── hit effecter (persistent) ─────────────────────────────────────────
        /// <summary>
        /// Ensures the persistent hit effecter is alive at <paramref name="cell"/>.
        /// Respawns if the cell changed.
        /// </summary>
        private void UpdateHitEffecter(IntVec3 cell, Map map)
        {
            if (Props.hitEffecter == null) return;
            if (_hitEffecter != null && _hitEffecterCell == cell) return; // already correct

            CleanupHitEffecter();
            // SpawnAttached requires a Thing; for a bare cell we manually create
            // the Effecter and set its offset without calling Trigger (which would
            // fire a one-shot burst immediately).
            _hitEffecter        = new Effecter(Props.hitEffecter);
            _hitEffecter.offset = Props.hitEffecterOffset;
            _hitEffecterCell    = cell;
        }

        private void TickHitEffecter()
        {
            if (_hitEffecter == null || !parent.Spawned) return;
            var ti = new TargetInfo(_hitEffecterCell, parent.Map);
            _hitEffecter.EffectTick(ti, ti);
        }

        private void CleanupHitEffecter()
        {
            _hitEffecter?.Cleanup();
            _hitEffecter     = null;
            _hitEffecterCell = IntVec3.Invalid;
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            CleanupActiveEffecter();
            CleanupHitEffecter();
            base.PostDeSpawn(map, mode);
        }

        // ── selection overlay ─────────────────────────────────────────────────
        public override void PostDrawExtraSelectionOverlays()
        {
            if (!parent.Spawned) return;

            IntVec3       dir   = parent.Rotation.FacingCell;
            List<IntVec3> cells = new List<IntVec3>(Props.range);

            for (int i = 1; i <= Props.range; i++)
            {
                IntVec3 cell = parent.Position + dir * i;
                if (!cell.InBounds(parent.Map)) break;
                cells.Add(cell);
            }

            if (cells.Count > 0)
                GenDraw.DrawFieldEdges(cells, Color.Lerp(Color.yellow, Color.red, 0.45f));
        }

        // ── inspect string ────────────────────────────────────────────────────
        public override string CompInspectStringExtra()
        {
            if (!active) return null;
            return "FFF_CompContinuousDamager_Remaining".Translate(
                ticksRemaining.ToStringTicksToPeriod());
        }
    }

    // ── properties ────────────────────────────────────────────────────────────
    public class CompProperties_ContinuousDamager : CompProperties
    {
        /// <summary>Which damage type to apply each hit.</summary>
        public DamageDef damageDef;

        /// <summary>Raw damage per hit (before armor).</summary>
        public float damageAmount = 50f;

        /// <summary>Armor penetration factor (0–1).</summary>
        public float armorPenetration = 0.5f;

        /// <summary>Total active duration in ticks.</summary>
        public int durationTicks = 600;          // 10 seconds at 60 tps

        /// <summary>Ticks between each damage application.</summary>
        public int tickInterval = 30;            // every 0.5 seconds

        /// <summary>How many cells in front to scan; hits the first occupied cell and stops.</summary>
        public int range = 5;

        /// <summary>Whether to show a direct activation gizmo (bypasses colonist interaction).</summary>
        public bool showGizmo = false;

        /// <summary>Optional icon path for the gizmo button.</summary>
        public string gizmoIcon = "UI/Commands/Attack";

        /// <summary>Sound played on activation (optional).</summary>
        public SoundDef activateSound;

        /// <summary>Effecter that runs continuously while the comp is active (attached to parent, aimed at front cell).</summary>
        public EffecterDef activeEffecter;

        /// <summary>
        /// How far forward (in world units) to shift the active effecter origin
        /// from the parent's centre toward the facing direction.
        /// 0.5 = front edge of a 1×1 building.
        /// </summary>
        public float activeEffecterForwardOffset = 0.5f;

        /// <summary>Effecter spawned on each hit location (optional).</summary>
        public EffecterDef hitEffecter;

        /// <summary>
        /// Additional world-space offset applied to the hit effecter spawn position
        /// relative to the centre of the hit cell (Y is ignored at spawn).
        /// </summary>
        public Vector3 hitEffecterOffset = Vector3.zero;

        public CompProperties_ContinuousDamager()
        {
            compClass = typeof(CompContinuousDamager);
        }

        public override void ResolveReferences(ThingDef parentDef)
        {
            base.ResolveReferences(parentDef);
            damageDef ??= DamageDefOf.Blunt;
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
                yield return e;

            if (damageDef == null)
                yield return $"{nameof(CompProperties_ContinuousDamager)}: damageDef is null.";
            if (damageAmount <= 0f)
                yield return $"{nameof(CompProperties_ContinuousDamager)}: damageAmount must be > 0.";
            if (durationTicks <= 0)
                yield return $"{nameof(CompProperties_ContinuousDamager)}: durationTicks must be > 0.";
            if (tickInterval <= 0)
                yield return $"{nameof(CompProperties_ContinuousDamager)}: tickInterval must be > 0.";
            if (range <= 0)
                yield return $"{nameof(CompProperties_ContinuousDamager)}: range must be > 0.";
        }
    }
}
