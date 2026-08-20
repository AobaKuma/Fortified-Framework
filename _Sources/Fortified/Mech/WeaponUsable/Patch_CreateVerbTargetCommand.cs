using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace Fortified
{
    // Drone（CompDrone / IWeaponUsable）的 ThingDef 沒有 CompOverseerSubject，
    // 而 1.6 原版的 MechanitorUtility.EverControllable = mech.OverseerSubject != null，
    // 導致 VerbTracker.CreateVerbTargetCommand 把遠程攻擊 gizmo 以
    // "CannotOrderNonControlled" 停用，玩家無法命令 Drone 進行遠程攻擊。
    //
    // 此補丁只在 CreateVerbTargetCommand 這一個 call site 放行玩家機械體，
    // 不改動 EverControllable 的全局語意（GetMechGizmos / CanControlMech /
    // Dialog_FormCaravan 等其他調用點不受影響）。
    // 陣營防護：IsColonyMech 內部硬檢查 Faction == Faction.OfPlayer，
    // 敵方/野化/奴隸 Drone 一律不放行。
    [HarmonyPatch(typeof(VerbTracker), "CreateVerbTargetCommand")]
    internal static class Patch_CreateVerbTargetCommand
    {
        static readonly MethodInfo EverControllable =
            AccessTools.Method(typeof(MechanitorUtility), nameof(MechanitorUtility.EverControllable));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction ci in instructions)
            {
                if (ci.opcode == OpCodes.Call && ci.OperandIs(EverControllable))
                {
                    // 把 call MechanitorUtility.EverControllable 換成我們的 wrapper，
                    // bool 進 bool 出，堆疊與後續 brtrue.s IL_00A0 完全相容。
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(Patch_CreateVerbTargetCommand), nameof(EverControllableOrDrone)));
                }
                else
                {
                    yield return ci;
                }
            }
        }

        public static bool EverControllableOrDrone(Pawn mech)
        {
            if (MechanitorUtility.EverControllable(mech))
            {
                return true;
            }
            // 只放行玩家陣營機械體；非玩家（敵方）Drone 在此被擋。
            if (mech == null || !mech.IsColonyMech)
            {
                return false;
            }
            return mech is IWeaponUsable || mech.TryGetComp<CompDrone>() != null;
        }
    }
}
