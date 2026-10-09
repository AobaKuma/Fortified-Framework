using CombatExtended;
using HarmonyLib;
using RimWorld;
using System.Linq;
using System.Reflection;
using Verse;

namespace FortifiedCE
{
    /// <summary>
    /// 原版 PlaceWorker_ShowTurretRadius.AllowsPlacing 用 Verbs.Find(...) 找砲塔槍身的射程，找不到回傳 null 就直接 NRE。
    /// CE 自己的 Harmony_PlaceWorker_ShowTurretRadius 把那個 lambda 放寬成「verbClass == Verb_ShootCE」，
    /// 但是是完全相等比較，Verb_ShootCE 的子類 (例如 Verb_ArcSprayProjectileCE) 會被漏掉，
    /// 放置預覽時 UIRootUpdate 每幀都丟 NRE。這裡再補上「是 Verb_ShootCE 或其子類」。
    /// </summary>
    [HarmonyPatch]
    internal static class Harmony_PlaceWorker_ShowTurretRadius
    {
        // 原版 lambda: <>c.<AllowsPlacing>b__0_0(VerbProperties v)，名稱隨編譯器而變，比照 CE 用特徵找
        private static MethodBase FindTarget()
        {
            return typeof(PlaceWorker_ShowTurretRadius)
                .GetNestedTypes(AccessTools.all)
                .SelectMany(t => t.GetMethods(AccessTools.all))
                .FirstOrDefault(m => m.Name.Contains("<AllowsPlacing>") && m.ReturnType == typeof(bool)
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(VerbProperties));
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            if (FindTarget() != null)
            {
                return true;
            }
            Log.Warning("[FortifiedCE] 找不到 PlaceWorker_ShowTurretRadius.AllowsPlacing 的 lambda，略過射程圈修補。");
            return false;
        }

        [HarmonyTargetMethod]
        private static MethodBase TargetMethod() => FindTarget();

        // __0 = 該 lambda 的第一個參數 (VerbProperties v)，用索引避免參數名稱改變
        [HarmonyPostfix]
        private static void Postfix(VerbProperties __0, ref bool __result)
        {
            if (!__result && __0.verbClass != null && typeof(Verb_ShootCE).IsAssignableFrom(__0.verbClass))
            {
                __result = true;
            }
        }
    }
}
