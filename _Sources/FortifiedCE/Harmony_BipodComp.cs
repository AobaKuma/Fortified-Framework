using CombatExtended;
using HarmonyLib;
using Verse;

namespace FortifiedCE
{
    /// <summary>
    /// CE 的 <c>BipodComp.ShouldSetUp</c> 直接把 ParentHolder 硬轉型：
    ///
    ///     Pawn pawn = ((Pawn_EquipmentTracker)ParentHolder).pawn;
    ///
    /// 但 <c>Verb.caster</c> 在卸下裝備時並不會被清空——vanilla 的
    /// <c>Verb.Notify_EquipmentLost</c> 只處理姿態與工作，不動 caster。
    /// 因此一把曾被裝備過的兩腳架武器被丟到地上或移進背包後，
    /// <c>Verb_ShootCE.VerbTickCE</c> 仍會拿著舊的 CasterPawn 呼叫 <c>SetUpStart</c>，
    /// 進而讀取 ShouldSetUp，並在轉型 Map / Pawn_InventoryTracker 時丟出
    /// InvalidCastException，且因為是 tick 路徑會每幀重複洗版。
    ///
    /// 本框架的機械體換武流程（MakeRoomFor 丟棄主手武器、CompVehicleWeapon 換裝、
    /// CompMinifyToInventory 收納等）會頻繁製造這個狀態，所以在此補上守衛：
    /// 武器不在裝備欄裡時一律回 false（沒人持握就不該自動架設），
    /// 有正常持握才放行走 CE 原本的邏輯。
    /// </summary>
    [HarmonyPatch(typeof(BipodComp), nameof(BipodComp.ShouldSetUp), MethodType.Getter)]
    internal static class Harmony_BipodComp_ShouldSetUp
    {
        [HarmonyPrefix]
        public static bool Prefix(BipodComp __instance, ref bool __result)
        {
            if (__instance?.parent?.ParentHolder is Pawn_EquipmentTracker)
            {
                return true;
            }
            __result = false;
            return false;
        }
    }
}
