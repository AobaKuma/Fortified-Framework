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
                // Only Bill_Production has the repeat-mode/status surface this subclass relies on.
                // Specialised bills (UFT, mech gestation, autonomous forming) must keep their own type.
                if (__result != null && __result.GetType() != typeof(Bill_Production))
                {
                    Log.WarningOnce(
                        $"[FFF] Recipe {recipe.defName} carries ModExt_EnvironmentalBill but produces a " +
                        $"{__result.GetType().Name}; environmental restrictions will not be applied.",
                        recipe.shortHash ^ 0x5EE7);
                    return;
                }
                __result = new Bill_Production_Environmental(recipe, precept);
            }
        }
    }
}