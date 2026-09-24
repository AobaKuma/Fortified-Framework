using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Fortified
{
    /// <summary>
    /// 從地表對入口電梯執行緊急解鎖。結構照原版 JobDriver_Hack：走過去、每 tick 以 HackingSpeed 推進。
    /// 不走 CompHackable：地表電梯本身已經掛著進門用的 CompHackable（封鎖時歸零、解鎖完成時補滿），
    /// 同一個 Thing 掛兩個 CompHackable 會讓原版取錯。
    ///
    /// Emergency override on the surface lift, laid out like vanilla JobDriver_Hack: walk over, then advance
    /// by HackingSpeed each tick. Not a CompHackable: the lift already carries one for getting in (wiped by the
    /// lockdown, completed by this override), and a second CompHackable on the same thing would confuse
    /// vanilla's lookup.
    /// </summary>
    public class JobDriver_LockdownOverride : JobDriver
    {
        private Thing Target => job.GetTarget(TargetIndex.A).Thing;

        private CompFacilityLockdownGate Gate => Target?.TryGetComp<CompFacilityLockdownGate>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Target, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => Gate == null || !Gate.OverrideAvailable);

            PathEndMode pathEndMode = Target.def.hasInteractionCell ? PathEndMode.InteractionCell : PathEndMode.ClosestTouch;
            yield return Toils_Goto.GotoThing(TargetIndex.A, pathEndMode);

            Toil work = ToilMaker.MakeToil("LockdownOverride");
            work.handlingFacing = true;
            work.tickAction = delegate
            {
                Gate.DoOverrideWork(pawn, pawn.GetStatValue(StatDefOf.HackingSpeed));
                pawn.skills?.Learn(SkillDefOf.Intellectual, 0.1f);
                pawn.rotationTracker.FaceTarget(Target);
            };
            work.WithEffect(EffecterDefOf.Hacking, TargetIndex.A);
            work.WithProgressBar(TargetIndex.A, () => Gate?.OverrideProgressPct ?? 0f, interpolateBetweenActorAndTarget: false, -0.5f, alwaysShow: true);
            work.PlaySoundAtStart(SoundDefOf.Hacking_Started);
            work.PlaySustainerOrSound(SoundDefOf.Hacking_InProgress);
            work.AddFinishAction(delegate
            {
                if (Gate != null && !Gate.OverrideAvailable)
                {
                    SoundDefOf.Hacking_Completed.PlayOneShot(Target);
                }
            });
            work.FailOnCannotTouch(TargetIndex.A, pathEndMode);
            work.defaultCompleteMode = ToilCompleteMode.Never;
            work.activeSkill = () => SkillDefOf.Intellectual;
            yield return work;
        }
    }
}
