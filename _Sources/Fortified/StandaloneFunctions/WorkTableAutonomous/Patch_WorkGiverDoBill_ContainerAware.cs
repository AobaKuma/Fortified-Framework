using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 讓殖民者搬料前的搜尋把 <see cref="Building_WorkTableAutonomous"/> 容器裡已有的料算進去。
    /// </summary>
    /// <remarks>
    /// 原版 WorkGiver_DoBill 只對「原版的」Building_WorkTableAutonomous 才會把 innerContainer 加進候選，
    /// FFF 的機台繼承自 Building_WorkTable，所以殖民者一律照整份配方再搬一次，
    /// 容器裡的殘料（自動抽料中斷、找不到掛名製作者時留下的）因此重複。
    /// 這裡補上與原版一致的行為：容器內的料優先採用，只搬不足的部分；
    /// 已在容器裡的料 JobDriver_DoBill.CollectIngredientsToils 會自動跳過搬運。
    ///
    /// 限制：容器只有「一部分」料、而地圖上又湊不齊整份配方時，原版搜尋會先失敗，這裡不處理
    /// （容器湊得齊整份時由 Prefix 處理）。
    /// </remarks>
    [HarmonyPatch]
    public static class Patch_WorkGiverDoBill_ContainerAware
    {
        private static MethodBase target;

        // 目標是 private static，簽章隨版本可能變動；找不到就整個略過，不中斷啟動。
        public static bool Prepare()
        {
            target = AccessTools.Method(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients");
            if (target == null)
            {
                Log.Warning("[FFF] 找不到 WorkGiver_DoBill.TryFindBestBillIngredients，略過「搬料搜尋計入機台容器」的 patch。");
                return false;
            }
            return true;
        }

        public static MethodBase TargetMethod() => target;

        // 容器本身就湊得齊整份配方：直接用，不必出門找。
        [HarmonyPrefix]
        public static bool Prefix(Bill bill, Thing billGiver, List<ThingCount> chosen, ref bool __result)
        {
            if (billGiver is Building_WorkTableAutonomous table
                && table.innerContainer != null && table.innerContainer.Count > 0
                && LinkedStorageIngredientPuller.TrySelectFromContainer(table, bill, chosen))
            {
                __result = true;
                return false;
            }
            return true;
        }

        // 容器不夠但地圖湊得齊：重新挑一次，容器內的料優先，只搬不足的部分。
        [HarmonyPostfix]
        public static void Postfix(Bill bill, Thing billGiver, List<ThingCount> chosen, bool __result)
        {
            if (__result && billGiver is Building_WorkTableAutonomous table
                && table.innerContainer != null && table.innerContainer.Count > 0)
            {
                LinkedStorageIngredientPuller.TryMergeContainerInto(table, bill, chosen);
            }
        }
    }
}
