using RimWorld;
using Verse;

namespace Fortified
{
    // 叠加疼痛系数
    // 汇总疼痛来源
    public class StatPart_PainFactor : StatPart
    {
        // 疼痛偏移曲线
        public SimpleCurve curve;

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!(req.Thing is Pawn pawn)) return;
            if (curve == null || !pawn.RaceProps.IsFlesh) return;

            val += curve.Evaluate(CalcPainFactor(pawn));
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!(req.Thing is Pawn pawn)) return null;
            if (curve == null || !pawn.RaceProps.IsFlesh) return null;

            float factor = CalcPainFactor(pawn);
            float offset = curve.Evaluate(factor);
            string offsetStr = offset >= 0f ? $"+{offset.ToStringPercent()}" : offset.ToStringPercent();
            return $"{"Pain".Translate()} x{factor.ToStringPercent()}: {offsetStr}";
        }

        // 计算疼痛倍率
        private static float CalcPainFactor(Pawn pawn)
        {
            float factor = 1f;

            var hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
                factor *= hediffs[i].PainFactor;

            if (pawn.genes != null)
                factor *= pawn.genes.PainFactor;

            if (pawn.story?.traits != null)
            {
                var traits = pawn.story.traits.allTraits;
                for (int i = 0; i < traits.Count; i++)
                    factor *= traits[i].CurrentData.painFactor;
            }

            return factor;
        }
    }
}
