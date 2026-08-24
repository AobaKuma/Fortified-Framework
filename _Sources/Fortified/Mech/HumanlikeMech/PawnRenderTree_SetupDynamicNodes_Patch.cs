using Verse;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using System.Linq;
using System;

namespace Fortified
{
    /// <summary>
    /// 讓人形機兵能夠顯示衣服的補丁。
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderTree), "SetupDynamicNodes")]
    public class PawnRenderTree_SetupDynamicNodes_Patch
    {
        public static void Postfix(PawnRenderTree __instance)
        {
            if (__instance.pawn is not IHumanlikeMech) return;

            FieldInfo dynField = AccessTools.Field(__instance.GetType(), "dynamicNodeTypeInstances");
            List<DynamicPawnRenderNodeSetup> dynamicNodeTypeInstance = dynField.GetValue(__instance) as List<DynamicPawnRenderNodeSetup>;
            if (dynamicNodeTypeInstance == null)
                return;

            foreach (DynamicPawnRenderNodeSetup node in dynamicNodeTypeInstance.Where(n => n.HumanlikeOnly))
            {
                foreach (var (child, parent) in node.GetDynamicNodes(__instance.pawn, __instance))
                {
                    var addChildMethod = AccessTools.Method(__instance.GetType(), "AddChild");
                    addChildMethod.Invoke(__instance, new object[] { child, parent });
                    //Log.Message("Patched");
                }
            }
        }
    }
    [HarmonyPatch(typeof(PawnRenderTree), "ShouldAddNodeToTree")]
    static class PawnRenderTree_ShouldAddNodeToTree_Patch
    {
        private static void Postfix(ref bool __result, PawnRenderNodeProperties props, Pawn ___pawn)
        {
            if (__result) return;
            if (___pawn is IHumanlikeMech && (props.workerClass == typeof(PawnRenderNodeWorker_Apparel_Body) || props.workerClass == typeof(PawnRenderNodeWorker_Apparel_Head)))
            {
                __result = true;
                return;
            }
        }
    }
}