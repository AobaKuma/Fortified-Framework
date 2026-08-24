using Verse;
using HarmonyLib;
using System.Reflection;
using RimWorld;

namespace Fortified
{
    //由於死掉的Pawn如果有Story的話就會顯示，但人形機沒有完整story所以會NRE。
    [HarmonyPatch(typeof(ITab_Pawn_Character), "get_IsVisible")]
    public static class Patch_ITab_Pawn_Character_IsVisible
    {
        public static void Postfix(ITab_Pawn_Character __instance, ref bool __result)
        {
            if (__result)
            {
                var pawn = AccessTools.Property(typeof(ITab_Pawn_Character), "PawnToShowInfoAbout")?.GetValue(__instance) as Pawn;
                __result = pawn is not IHumanlikeMech;
            }
        }
    }
}