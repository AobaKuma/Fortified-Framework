using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Fortified
{
    public class CompProperties_SubstationPower : CompProperties_Power
    {
        /// <summary>Minimum Intellect skill level to shut down without risk of electric shock.</summary>
        public int safeShutdownSkillLevel = 8;

        /// <summary>Probability of electric shock when Intellect is below <see cref="safeShutdownSkillLevel"/>.</summary>
        public float shockChance = 0.45f;

        /// <summary>Raw damage dealt to the pawn on a shock event.</summary>
        public float shockDamageAmount = 18f;

        /// <summary>
        /// 關閉時順手關掉一座同地圖、<c>building.buildingTags</c> 含這個標籤、<see cref="CompFlickable"/> 還開著的建築，
        /// 挑離變電箱最近的；空 = 不關。
        /// On shutdown, also switch off one building on the same map whose <c>building.buildingTags</c> contain this tag
        /// and whose <see cref="CompFlickable"/> is still on, picking the nearest; empty = none.
        /// </summary>
        public string flickOffBuildingTag;

        /// <summary>
        /// 從最近的幾座裡隨機挑一座。1 = 就是最近那座（一樣近時隨機）。
        /// Pick at random among this many nearest candidates. 1 = the nearest one (random among equally near).
        /// </summary>
        public int flickOffNearestCount = 1;

        public CompProperties_SubstationPower()
        {
            compClass = typeof(CompSubstationPower);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
                yield return e;
            if (safeShutdownSkillLevel < 0 || safeShutdownSkillLevel > 20)
                yield return $"{nameof(CompProperties_SubstationPower)}: safeShutdownSkillLevel must be 0–20.";
            if (shockChance < 0f || shockChance > 1f)
                yield return $"{nameof(CompProperties_SubstationPower)}: shockChance must be 0–1.";
            if (shockDamageAmount < 0f)
                yield return $"{nameof(CompProperties_SubstationPower)}: shockDamageAmount must be >= 0.";
            if (flickOffNearestCount < 1)
                yield return $"{nameof(CompProperties_SubstationPower)}: flickOffNearestCount must be >= 1.";
        }
    }

    /// <summary>
    /// Power plant comp for the underground substation cabinet.
    /// Generates a fixed wattage, can be shut down via the vanilla Hack job
    /// (with an Intellect shock risk for low-skill operators), and is
    /// temporarily disabled by EMP. A shutdown is permanent and can also switch
    /// off the nearest tagged building (see <see cref="CompProperties_SubstationPower.flickOffBuildingTag"/>).
    /// </summary>
    public class CompSubstationPower : CompPowerPlant
    {
        // ElectricalBurn is [MayRequireAnomaly] — fall back to Burn when absent.
        private static DamageDef ShockDamageDef =>
            DamageDefOf.ElectricalBurn
            ?? DefDatabase<DamageDef>.GetNamed("ElectricalBurn", errorOnFail: false)
            ?? DamageDefOf.Burn;

        // ── state ─────────────────────────────────────────────────────────────────
        private bool isShutdown;
        private CompStunnable stunnableComp;

        // ── accessors ─────────────────────────────────────────────────────────────
        public new CompProperties_SubstationPower Props =>
            (CompProperties_SubstationPower)props;

        public bool IsShutdown => isShutdown;

        public bool IsEMPDisabled =>
            stunnableComp != null
            && stunnableComp.StunHandler.Stunned
            && stunnableComp.StunHandler.StunFromEMP;

        // ── lifecycle ─────────────────────────────────────────────────────────────
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            stunnableComp = parent.TryGetComp<CompStunnable>();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref isShutdown, "isShutdown", false);
        }

        // ── power output ──────────────────────────────────────────────────────────
        public override void UpdateDesiredPowerOutput()
        {
            if (isShutdown || IsEMPDisabled || !base.PowerOn)
                base.PowerOutput = 0f;
            else
                base.PowerOutput = DesiredPowerOutput;
        }

        // ── hack callback ─────────────────────────────────────────────────────────
        /// <summary>
        /// Called by CompHackable.OnHacked when the vanilla Hack job completes.
        /// Applies the electric-shock risk, then shuts the unit down.
        /// </summary>
        public override void Notify_Hacked(Pawn hacker = null)
        {
            if (hacker != null)
                TryApplyShock(hacker);

            ExecuteShutdown();
        }

        // ── public API ────────────────────────────────────────────────────────────
        /// <summary>永久關閉；已經關著時什麼都不做。Shuts down for good; does nothing if already off.</summary>
        public void ExecuteShutdown()
        {
            if (isShutdown) return;
            isShutdown = true;
            UpdateDesiredPowerOutput();
            TryFlickOffNearestTagged();
        }

        // ── inspect string ────────────────────────────────────────────────────────
        public override string CompInspectStringExtra()
        {
            string baseStr = base.CompInspectStringExtra();

            string statusLine;
            if (IsEMPDisabled)
                statusLine = "FFF_Substation_EMPDisabled".Translate();
            else if (isShutdown)
                statusLine = "FFF_Substation_Offline".Translate();
            else
                statusLine = "FFF_Substation_Online".Translate();

            return baseStr.NullOrEmpty() ? statusLine : statusLine + "\n" + baseStr;
        }

        // ── helpers ───────────────────────────────────────────────────────────────
        private void TryFlickOffNearestTagged()
        {
            string tag = Props.flickOffBuildingTag;
            if (tag.NullOrEmpty() || !parent.Spawned) return;

            IntVec3 origin = parent.Position;
            List<CompFlickable> candidates = new List<CompFlickable>();
            foreach (Thing t in parent.Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                if (t == parent) continue;
                List<string> tags = t.def.building?.buildingTags;
                if (tags == null || !tags.Contains(tag)) continue;
                CompFlickable flickable = t.TryGetComp<CompFlickable>();
                if (flickable == null || !flickable.SwitchIsOn) continue;
                candidates.Add(flickable);
            }
            if (candidates.Count == 0) return;

            // 先打亂再依距離排序，一樣近的就是隨機順序。Shuffle first so equally near candidates end up in random order.
            candidates.Shuffle();
            candidates.SortBy(f => f.parent.Position.DistanceToSquared(origin));
            CompFlickable picked = candidates[Rand.Range(0, Math.Min(Props.flickOffNearestCount, candidates.Count))];

            // wantSwitchOn 一起改，免得留下「想被打開」的待撥狀態。Keep wantSwitchOn in step so no pending flick back on is left.
            picked.wantSwitchOn = false;
            picked.SwitchIsOn = false;

            // 還在迷霧裡的不帶玩家過去看，免得洩漏位置。Don't jump the camera to a building still under fog.
            Thing building = picked.parent;
            Messages.Message(
                "FFF_Substation_FlickedOffMessage".Translate(parent.Named("SUBSTATION"), building.Named("BUILDING")),
                building.Fogged() ? parent : building,
                MessageTypeDefOf.PositiveEvent);
        }

        private void TryApplyShock(Pawn hacker)
        {
            if (hacker.skills == null) return;

            int intellect = hacker.skills.GetSkill(SkillDefOf.Intellectual)?.Level ?? 0;
            if (intellect >= Props.safeShutdownSkillLevel) return;
            if (!Rand.Chance(Props.shockChance)) return;

            DamageInfo dinfo = new DamageInfo(
                ShockDamageDef,
                Props.shockDamageAmount,
                armorPenetration: 0f,
                angle: -1f,
                instigator: parent);

            hacker.TakeDamage(dinfo);

            Messages.Message(
                "FFF_Substation_ShockMessage".Translate(hacker.Named("PAWN")),
                hacker,
                MessageTypeDefOf.NegativeEvent);
        }
    }
}
