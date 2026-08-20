using HarmonyLib;
using RimWorld;
using Verse;

namespace Fortified
{
    public class Harmony_BillUtility
    {
        [HarmonyPatch(typeof(BillUtility), "MakeNewBill")]
        static class MakeNewBill_PostFix
        {
            [HarmonyPostfix]
            static void PostFix(RecipeDef recipe, Precept_ThingStyle precept, ref Bill __result)
            {
                if (recipe == null || !recipe.HasModExtension<ModExt_EnvironmentalBill>())
                {
                    return;
                }
                // Already ours (another patch or a re-entrant call) — leave it alone.
                if (__result is Bill_Production_Environmental)
                {
                    return;
                }
                // This swap is cosmetic: it only exists so the bill UI can show the currently active
                // exemptions on its status line. Only Bill_Production has that surface, and specialised
                // bills (UFT, mech gestation/resurrection, autonomous forming) must keep their own type
                // or they lose the behaviour that made MakeNewBill pick them.
                //
                // Leaving them alone is safe: the environmental requirements are enforced by
                // EnvironmentalBillGate through a postfix on every ShouldDoNow implementation
                // (Harmony_Bill_ShouldDoNow), not by this class. Those bills simply do not get the
                // extra status line.
                if (__result != null && __result.GetType() != typeof(Bill_Production))
                {
                    return;
                }
                __result = new Bill_Production_Environmental(recipe, precept);
            }
        }
    }
}
