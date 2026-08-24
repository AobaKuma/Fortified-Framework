using HarmonyLib;
using Verse;

namespace Fortified
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.IsColonistPlayerControlled), MethodType.Getter)]
    public static class Patch_Pawn_IsColonistPlayerControlled
    {
        public static void Postfix(ref bool __result, Pawn __instance)
        {
            if (!__result)
            {
                if (__instance is IHumanlikeMech && __instance.IsColonyMechPlayerControlled)
                {
                    __result = true;
                }
            }
        }
    }
}
