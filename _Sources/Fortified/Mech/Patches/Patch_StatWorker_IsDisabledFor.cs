using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{
    //由於衣服需要有身形，身形需要Story，但Story會使Stat出現不該有的東西。所以需要這個補丁。
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.IsDisabledFor))]
    public static class Patch_StatWorker_IsDisabledFor
    {
        static readonly FieldInfo story = AccessTools.Field(typeof(Pawn), nameof(Pawn.story));
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            //Log.Message("Patching Lady");

            Func<CodeInstruction, bool> predicate = (ci) => ci.opcode == OpCodes.Ldfld && ci.OperandIs(story);            
            foreach(CodeInstruction instr in instructions) {
                if (predicate(instr))
                {
                    //Log.Message("Find Target");
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_StatWorker_IsDisabledFor), nameof(IsStoryEmptyOrHumanMech)));
                }
                else
                {
                    yield return instr;
                }
            }
        }

        public static bool IsStoryEmptyOrHumanMech(this Pawn p) 
        {
            return p.story != null && p is not IHumanlikeMech;
        }
    }
}
