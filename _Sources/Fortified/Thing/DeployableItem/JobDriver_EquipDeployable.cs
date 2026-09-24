using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Fortified
{
    /// <summary>
    /// 走到部署中的可部署武器（陣地建築）旁，迷你化後直接裝備到主手。
    /// 與 CompUsable 的「拾起」流程不同：主手已有武器時會先卸下原武器，而不是把東西塞進背包。
    /// 由 <see cref="FloatMenuUtility"/> 的「裝備」浮動選單下達。
    /// </summary>
    public class JobDriver_EquipDeployable : JobDriver
    {
        private Thing Target => job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            // 走到一半對方被宣稱回去／易主時中止，與浮動選單的判定保持一致。
            this.FailOn(() => !CompMinifyToInventory.CanPickUp(Target, pawn).Accepted);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch).FailOnDespawnedNullOrForbidden(TargetIndex.A);

            Toil equip = ToilMaker.MakeToil("EquipDeployable");
            equip.initAction = delegate
            {
                Thing target = Target;
                if (target == null || target.Destroyed)
                {
                    return;
                }
                if (target.TryGetComp<CompMinifyToInventory>() == null)
                {
                    return;
                }
                CompMinifyToInventory.TryMinifyAndEquip(pawn, target);
            };
            equip.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return equip;
        }
    }
}
