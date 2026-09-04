using HarmonyLib;
using RimWorld;
using System;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{

    [HarmonyPatch(typeof(PawnComponentsUtility), "AddComponentsForSpawn")]
    public static class Patch_MechInteracte
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn)
        {
            //woken DMS 機械走快取 comps。
            if (pawn.CachedDeadManSwitch()?.woken == true)
            {
                if (pawn.interactions == null)
                {
                    pawn.interactions = new Pawn_InteractionsTracker(pawn);
                }
            }
        }
    }
}
