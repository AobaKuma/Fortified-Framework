using System.Linq;
using Fortified;
using RimWorld;
using Verse;

namespace AerospaceSample
{
    /// <summary>
    /// 導引頭：鎖定輸出最大的發電設施。在 MissilePartDef 寫
    /// <c>&lt;guidanceType Class="AerospaceSample.GuidanceType_PowerPlant" /&gt;</c>。
    /// 找不到目標時回傳 <see cref="IntVec3.Invalid"/>，FFF 會改用慣性落點。
    /// <see cref="GuidanceType.SpreadBest{T}"/> 讓同一波的導彈分散到不同目標。
    ///
    /// Seeker that homes on the strongest power plant. Return IntVec3.Invalid to fall back to inertial guidance.
    /// </summary>
    public class GuidanceType_PowerPlant : GuidanceType
    {
        /// <summary>低於這個輸出（W）的發電設施不算目標。</summary>
        public float minOutput = 500f;

        public override bool CanRetargetInTerminal => true;

        public override string MechanismDescription => "AerospaceSample.Guidance.PowerPlant".Translate(minOutput.ToString("0"));

        public override IntVec3 PickTarget(MissileSeekContext context)
        {
            Building best = SpreadBest(context,
                HostileBuildings(context).Where(b => Output(b) >= minOutput && Strikable(context, b.Position)),
                (a, b) => Output(a) > Output(b));
            return best != null ? AimCell(context, best) : IntVec3.Invalid;
        }

        private static float Output(Building b)
        {
            CompPowerPlant plant = b.GetComp<CompPowerPlant>();
            return plant != null && plant.PowerOn ? plant.PowerOutput : 0f;
        }
    }
}
