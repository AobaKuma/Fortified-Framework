using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Fortified
{
    // 范围内敌人定期概率惊慌
    public class CompCausePanicInRange : ThingComp
    {
        private int ticksSinceScan;

        public CompProperties_CausePanicInRange Props =>
            (CompProperties_CausePanicInRange)props;

        // 实际触发的状态
        private MentalStateDef StateDef =>
            Props.mentalState ?? FFF_DefOf.FFF_FleeInPlace;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad)
                ticksSinceScan = parent.thingIDNumber % Props.tickInterval;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksSinceScan, nameof(ticksSinceScan), 0);
        }

        public override void CompTickInterval(int delta)
        {
            if (!parent.Spawned) return;

            ticksSinceScan += delta;
            if (ticksSinceScan < Props.tickInterval) return;

            ticksSinceScan = 0;
            ScanAndPanic();
        }

        // 检查施加者眩晕
        private bool IsCasterStunned()
        {
            if (parent is Pawn pawn)
                return pawn.stances?.stunner?.Stunned ?? false;
            return false;
        }

        // 扫描敌对目标并触发
        private void ScanAndPanic()
        {
            if (!(parent is IAttackTargetSearcher searcher)) return;
            if (Props.suppressWhenCasterStunned && IsCasterStunned()) return;

            float rangeSq = Props.range * Props.range;
            IntVec3 origin = parent.Position;

            // 读取敌对缓存
            List<IAttackTarget> targets =
                parent.Map.attackTargetsCache.GetPotentialTargetsFor(searcher);

            for (int i = 0; i < targets.Count; i++)
            {
                if (!(targets[i].Thing is Pawn pawn)) continue;
                if (pawn.Position.DistanceToSquared(origin) > rangeSq) continue;
                if (!IsValidVictim(pawn)) continue;
                if (!Rand.Chance(CalcChance(pawn))) continue;

                pawn.mindState.mentalStateHandler.TryStartMentalState(
                    StateDef, causedByDamage: true, otherPawn: parent as Pawn);
            }
        }

        // 计算对目标的实际触发概率
        // 合成恐慌概率
        private float CalcChance(Pawn victim)
        {
            float sum = 0f;
            sum += Props.bodySizeWeight * (BodySizeFactor(victim) - 1f);
            sum += Props.breakdownProximityWeight * (BreakdownProximityFactor(victim) - 1f);

            float raw = Props.chancePerScan * (1f + sum);

            // 应用恐惧抗性
            float resistance = victim.GetStatValue(FFF_DefOf.FFF_FearResistance);
            return Mathf.Clamp01(raw * (1f - resistance));
        }

        // 计算体型诱因
        private float BodySizeFactor(Pawn victim)
        {
            if (!Props.useBodySizeModifier) return 1f;

            float casterSize = (parent is Pawn p) ? p.BodySize : Props.defaultCasterBodySize;
            float ratio = casterSize / Mathf.Max(victim.BodySize, 0.01f);

            if (Props.bodySizeCurve != null)
                return Props.bodySizeCurve.Evaluate(ratio);
            return Mathf.Clamp(ratio * Props.bodySizeModifierScale,
                Props.bodySizeMultiplierMin, Props.bodySizeMultiplierMax);
        }

        // 计算心情诱因
        private float BreakdownProximityFactor(Pawn victim)
        {
            if (!Props.useBreakdownProximity) return 1f;
            if (Props.moodProximityCurve == null) return 1f;

            MentalBreaker breaker = victim.mindState?.mentalBreaker;
            if (breaker == null) return 1f;

            float proximity = breaker.CurMood / Mathf.Max(breaker.BreakThresholdMinor, 0.01f);
            return Props.moodProximityCurve.Evaluate(proximity);
        }

        // 判定可否被赋予状态
        private bool IsValidVictim(Pawn pawn)
        {
            if (pawn == parent || pawn.Dead || !pawn.Spawned || !pawn.RaceProps.IsFlesh) return false;
            if (pawn.InMentalState) return false;
            return StateDef.Worker.StateCanOccur(pawn);
        }
    }

    public class CompProperties_CausePanicInRange : CompProperties
    {
        // 扫描间隔
        public int tickInterval = 300;

        // 影响半径
        public float range = 5f;

        // 每次扫描每人触发概率
        public float chancePerScan = 1f;

        // 赋予的心理状态
        public MentalStateDef mentalState;

        // 眩晕时压制效果
        public bool suppressWhenCasterStunned = true;

        // 体型差诱因权重
        public float bodySizeWeight = 1f;

        // 心情接近度诱因权重
        public float breakdownProximityWeight = 1f;

        // 是否启用体型差概率修正
        public bool useBodySizeModifier = false;

        // 体型比的缩放系数
        public float bodySizeModifierScale = 1f;

        // 非小人默认体型
        public float defaultCasterBodySize = 1f;

        // 体型比倍率下限
        public float bodySizeMultiplierMin = 0f;

        // 体型比倍率上限
        public float bodySizeMultiplierMax = 2f;

        // 体型概率曲线
        // 替代线性方案
        public SimpleCurve bodySizeCurve;

        // 是否启用心情接近度概率修正
        public bool useBreakdownProximity = false;

        // 心情修正曲线
        public SimpleCurve moodProximityCurve;

        public CompProperties_CausePanicInRange()
        {
            compClass = typeof(CompCausePanicInRange);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
                yield return e;

            if (tickInterval <= 0)
                yield return $"{nameof(CompProperties_CausePanicInRange)}: tickInterval must be > 0.";
            if (range <= 0f)
                yield return $"{nameof(CompProperties_CausePanicInRange)}: range must be > 0.";
            if (chancePerScan <= 0f)
                yield return $"{nameof(CompProperties_CausePanicInRange)}: chancePerScan must be > 0.";
            if (useBodySizeModifier && bodySizeMultiplierMin > bodySizeMultiplierMax)
                yield return $"{nameof(CompProperties_CausePanicInRange)}: bodySizeMultiplierMin must be <= bodySizeMultiplierMax.";
        }
    }
}
