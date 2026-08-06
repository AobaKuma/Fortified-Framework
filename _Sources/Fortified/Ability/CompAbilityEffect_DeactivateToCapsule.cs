using RimWorld;
using Verse;

namespace Fortified
{
    // 原地封存：把施法的機兵自己轉為休眠容器建築。
    //
    // 與 CompMechDeactivate 的差別只在於觸發介面 —— 那個是常駐 Gizmo，
    // 這個是 AbilityDef，因此可以掛冷卻、暖機時間、AI 使用條件與自訂圖標。
    // 實際的容器選型與收納流程共用 MechCapsuleUtility.DeactivateMech()。
    //
    // 注意：Apply() 會讓 parent.pawn 直接 DeSpawn，因此不要在 base.Apply()
    // 之後再碰 pawn 的地圖狀態。
    public class CompAbilityEffect_DeactivateToCapsule : CompAbilityEffect
    {
        public new CompProperties_AbilityDeactivateToCapsule Props =>
            (CompProperties_AbilityDeactivateToCapsule)props;

        private Pawn Caster => parent?.pawn;

        public override bool CanCast => base.CanCast && CanDeactivateNow(out _);

        // 統一的可用性判定，順便回傳不可用的原因供 UI 顯示
        private bool CanDeactivateNow(out string reason)
        {
            reason = null;
            Pawn pawn = Caster;

            if (pawn == null || !pawn.Spawned || pawn.Dead)
            {
                reason = "FFF.Mothball.Reason.NotSpawned".Translate();
                return false;
            }
            if (!pawn.RaceProps.IsMechanoid)
            {
                reason = "FFF.Mothball.Reason.NotMech".Translate();
                return false;
            }
            if (Props.playerOnly && pawn.Faction != Faction.OfPlayer)
            {
                reason = "FFF.Mothball.Reason.NotPlayer".Translate();
                return false;
            }
            // 戰鬥中封存等同於棄械，預設禁止
            if (!Props.allowWhileDrafted && pawn.Drafted)
            {
                reason = "FFF.Mothball.Reason.Drafted".Translate();
                return false;
            }
            if (!Props.allowWhileDowned && pawn.Downed)
            {
                reason = "FFF.Mothball.Reason.Downed".Translate();
                return false;
            }
            if (MechCapsuleUtility.GetCapsuleDefForMech(pawn) == null)
            {
                reason = "FFF.Mothball.Reason.NoCapsule".Translate();
                return false;
            }
            return true;
        }

        // 滑鼠移上去時說明為何不能用
        public override bool GizmoDisabled(out string reason)
        {
            if (!CanDeactivateNow(out string why))
            {
                reason = why;
                return true;
            }
            return base.GizmoDisabled(out reason);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn pawn = Caster;
            if (pawn == null || !pawn.Spawned || pawn.Dead) return;

            string label = pawn.LabelShortCap;
            Building_MechCapsule capsule = MechCapsuleUtility.DeactivateMech(pawn);
            if (capsule == null) return;

            if (Props.effecterOnDeactivate != null && capsule.Spawned)
            {
                Effecter eff = Props.effecterOnDeactivate.Spawn(capsule.Position, capsule.Map);
                eff.Cleanup();
            }
            if (Props.sendMessage)
            {
                Messages.Message("FFF.MechDeactivated".Translate(label), capsule,
                    MessageTypeDefOf.NeutralEvent);
            }
        }
    }

    public class CompProperties_AbilityDeactivateToCapsule : CompProperties_AbilityEffect
    {
        // 僅玩家派系可用
        public bool playerOnly = true;

        // 允許在徵召狀態下封存（預設否：戰鬥中封存等同棄械）
        public bool allowWhileDrafted = false;

        // 允許在倒地狀態下封存
        public bool allowWhileDowned = false;

        // 是否發出訊息
        public bool sendMessage = true;

        // 封存完成時的特效
        public EffecterDef effecterOnDeactivate;

        public CompProperties_AbilityDeactivateToCapsule()
        {
            compClass = typeof(CompAbilityEffect_DeactivateToCapsule);
        }
    }
}
