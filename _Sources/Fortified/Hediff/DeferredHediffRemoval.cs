using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 死亡通知期間的安全移除。原版 HediffSet.Notify_PawnDied 用索引倒著走 hediffs，期間若有 hediff 一次移除好幾個
    /// （或移除排在後面的），索引就會超出範圍（"Error while killing … ArgumentOutOfRangeException"）。
    /// 在通知期間呼叫 <see cref="Remove"/> 只會排隊，等原版迴圈跑完才真的移除；其他時候立刻移除。
    ///
    /// Safe removal during death notifications. Vanilla HediffSet.Notify_PawnDied walks hediffs backwards by index; a
    /// hediff that removes several others (or ones after itself) mid-loop sends the index out of range ("Error while
    /// killing … ArgumentOutOfRangeException"). During the notification <see cref="Remove"/> only queues; the removal
    /// happens once vanilla's loop is done. At any other time it removes straight away.
    /// </summary>
    public static class DeferredHediffRemoval
    {
        private static int depth;
        private static readonly List<(Pawn pawn, Hediff hediff)> pending = new List<(Pawn, Hediff)>();

        public static bool InDeathNotify => depth > 0;

        public static void Remove(Pawn pawn, Hediff hediff)
        {
            if (pawn?.health?.hediffSet == null || hediff == null) return;
            if (depth > 0)
            {
                pending.Add((pawn, hediff));
                return;
            }
            if (pawn.health.hediffSet.hediffs.Contains(hediff)) pawn.health.RemoveHediff(hediff);
        }

        internal static void Enter() => depth++;

        internal static void Exit()
        {
            depth = Math.Max(0, depth - 1);
            if (depth > 0 || pending.Count == 0) return;
            List<(Pawn pawn, Hediff hediff)> batch = new List<(Pawn, Hediff)>(pending);
            pending.Clear();
            foreach ((Pawn pawn, Hediff hediff) in batch)
            {
                try
                {
                    if (pawn.health?.hediffSet != null && pawn.health.hediffSet.hediffs.Contains(hediff)) pawn.health.RemoveHediff(hediff);
                }
                catch (Exception e)
                {
                    Log.Warning($"[FortifiedFramework] Deferred removal of {hediff.def?.defName} from {pawn} failed: {e.Message}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(HediffSet), nameof(HediffSet.Notify_PawnDied))]
    public static class Patch_HediffSet_Notify_PawnDied
    {
        public static void Prefix() => DeferredHediffRemoval.Enter();

        // Finalizer：即使通知途中擲例外也一定會出場並清掉排隊的移除。Runs even if the notification throws.
        public static Exception Finalizer(Exception __exception)
        {
            DeferredHediffRemoval.Exit();
            return __exception;
        }
    }
}
