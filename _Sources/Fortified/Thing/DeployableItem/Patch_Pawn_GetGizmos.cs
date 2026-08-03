using HarmonyLib;
using Verse;
using RimWorld;
using System;
using System.Collections.Generic;

namespace Fortified
{
    /// <summary>
    /// 把背包與裝備欄中實作 <see cref="IGizmoGiver"/> 的物件的 Gizmo 注入 pawn 的指令列。
    /// 資格判定改走 <see cref="DeployUtility.CanOperateDeployable"/>，
    /// 讓 <see cref="IWeaponUsable"/> 機械體不受 ToolUser 智力門檻限制。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_InventoryTracker), nameof(Pawn_InventoryTracker.GetGizmos))]
    internal static class Patch_Pawn_GetGizmos
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn_InventoryTracker __instance)
        {
            if (__result != null)
            {
                foreach (Gizmo g in __result) yield return g;
            }

            Pawn pawn = __instance?.pawn;
            if (pawn == null || !pawn.Spawned) yield break;
            if (Find.Selector?.SingleSelectedThing != pawn) yield break;
            if (pawn.Faction != Faction.OfPlayerSilentFail) yield break;
            if (!DeployUtility.CanOperateDeployable(pawn)) yield break;

            if (__instance.innerContainer != null)
            {
                foreach (Thing thing in __instance.innerContainer)
                {
                    Gizmo gizmo = TryGetGizmo(thing, pawn);
                    if (gizmo != null) yield return gizmo;
                }
            }

            if (pawn.equipment == null) yield break;

            foreach (Thing thing in pawn.equipment.AllEquipmentListForReading)
            {
                Gizmo gizmo = TryGetGizmo(thing, pawn);
                if (gizmo != null) yield return gizmo;
            }
        }

        /// <summary>
        /// 單一物件的 Gizmo 取得。第三方實作丟出例外時只記錄一次並跳過，
        /// 不讓整條指令列的迭代中斷。
        /// </summary>
        private static Gizmo TryGetGizmo(Thing thing, Pawn pawn)
        {
            if (thing is not IGizmoGiver giver)
            {
                return null;
            }
            try
            {
                return giver.GetGizmoForPawn(pawn);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[FFF] {thing.ToStringSafe()} 的 GetGizmoForPawn 發生例外：{ex}", thing.GetType().GetHashCode() ^ 0x0FF10002);
                return null;
            }
        }
    }
}
