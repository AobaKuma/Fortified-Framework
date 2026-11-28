using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>核融合爐對熱核推進器的支援額度（設計稿 §5-5(c)）。</summary>
    public class CompProperties_TransferReactor : CompProperties
    {
        /// <summary>每座可支援幾具 requiresReactorSupport 的引擎。</summary>
        public int supportedEngines = 2;

        public CompProperties_TransferReactor()
        {
            compClass = typeof(CompTransferReactor);
        }
    }

    public class CompTransferReactor : ThingComp
    {
        public CompProperties_TransferReactor Props => (CompProperties_TransferReactor)props;

        /// <summary>已生成、未損壞、開啟且（若有電力組件）運轉中。是否在船上由 <see cref="TransferShipSurvey"/> 判斷。</summary>
        public bool IsAvailable
        {
            get
            {
                if (!parent.Spawned)
                {
                    return false;
                }
                CompBreakdownable breakdownable = parent.GetComp<CompBreakdownable>();
                if (breakdownable != null && breakdownable.BrokenDown)
                {
                    return false;
                }
                CompFlickable flickable = parent.GetComp<CompFlickable>();
                if (flickable != null && !flickable.SwitchIsOn)
                {
                    return false;
                }
                CompPowerTrader power = parent.GetComp<CompPowerTrader>();
                return power == null || power.PowerOn;
            }
        }

        public override string CompInspectStringExtra()
        {
            if (!parent.Spawned)
            {
                return null;
            }
            PileNetwork net = MapComponent_PileNetwork.For(parent.Map)?.NetworkContaining(parent);
            if (net == null)
            {
                // 建在逆重船上（作為逆重船設施）時不顯示「不在船上」
                if (CompTransferEngine.IsOnGravshipSubstructure(parent))
                {
                    return null;
                }
                return TransferText.Key("FFF.Transfer.Reactor.NotOnShip", parent).Translate(Props.supportedEngines);
            }
            TransferShipSurvey survey = net.Survey;
            return TransferText.Key("FFF.Transfer.Reactor.Support", parent).Translate(Props.supportedEngines, survey.reactorDemand, survey.reactorCapacity);
        }
    }
}
