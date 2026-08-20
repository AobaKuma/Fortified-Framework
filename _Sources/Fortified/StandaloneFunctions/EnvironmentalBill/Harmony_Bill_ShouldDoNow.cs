using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace Fortified;

/// <summary>
/// Applies <see cref="EnvironmentalBillGate"/> to every bill, whatever its concrete type.
/// <para>
/// <c>Bill.ShouldDoNow</c> is abstract, so there is no single body to hook. Instead every non-abstract
/// declaration of it — <c>Bill_Production</c>, <c>Bill_Medical</c>, <c>Bill_Autonomous</c>, <c>Bill_Mech</c>
/// and anything a mod adds that is already loaded — gets the same postfix. Types that do not declare their
/// own override are covered by whichever ancestor does.
/// </para>
/// <para>
/// Overrides that chain up to their base run the postfix more than once per call; the gate memoises its
/// verdict per bill per tick and refuses to re-notify an already suspended bill, so that is harmless.
/// </para>
/// </summary>
[HarmonyPatch]
internal static class Harmony_Bill_ShouldDoNow
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        List<Type> candidates = new List<Type> { typeof(Bill) };
        candidates.AddRange(typeof(Bill).AllSubclasses());

        HashSet<MethodBase> emitted = new HashSet<MethodBase>();
        for (int i = 0; i < candidates.Count; i++)
        {
            Type type = candidates[i];
            if (type == null || type.IsGenericTypeDefinition || type.ContainsGenericParameters)
            {
                continue;
            }

            MethodInfo method = null;
            try
            {
                method = AccessTools.DeclaredMethod(type, nameof(Bill.ShouldDoNow));
            }
            catch (Exception e)
            {
                Log.Warning($"[FFF] Could not inspect {type} for ShouldDoNow, skipping it: {e.Message}");
                continue;
            }

            // Abstract declarations have no body to patch; a same-named member with a different shape
            // is somebody else's method, not the one we mean.
            if (method == null || method.IsAbstract || method.ReturnType != typeof(bool) ||
                method.GetParameters().Length != 0)
            {
                continue;
            }

            if (emitted.Add(method))
            {
                yield return method;
            }
        }
    }

    // Run last so that a bill another mod has already vetoed is left alone: the gate should never be the
    // thing that flips a "no" into extra work, and it must not suspend over a state we did not evaluate.
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Bill __instance, ref bool __result)
    {
        if (!__result)
        {
            return;
        }

        try
        {
            __result = EnvironmentalBillGate.CanDoNow(__instance);
        }
        catch (Exception e)
        {
            // A throwing work scan would break pawn job assignment for every bill on the map.
            // Fail loud but harmless: keep whatever the original method decided.
            Log.ErrorOnce($"[FFF] Environmental bill check threw for {__instance?.recipe?.defName ?? "null recipe"}: {e}",
                          0x5EE7 ^ (__instance?.recipe?.shortHash ?? 0));
        }
    }
}
