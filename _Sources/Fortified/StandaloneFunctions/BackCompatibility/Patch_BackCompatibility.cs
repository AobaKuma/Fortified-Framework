using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace Fortified;

/// <summary>
/// 依 DefRenameDef 把舊 defName 轉成新名稱, 保住舊存檔的研究進度等資料。
/// 掛 postfix 而非註冊 BackCompatibilityConverter: 原版在「遊戲版本與模組清單都與存檔相同」時
/// (CheckSaveIdenticalToCurrentEnvironment) 會跳過整條轉換鏈, 而只更新模組的玩家正好是這種情況。
/// </summary>
[HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.BackCompatibleDefName))]
public static class Patch_BackCompatibility_BackCompatibleDefName
{
    // (Def 類型, 舊 defName) -> 新 defName; DefRenameDef 載入進 DefDatabase 後才建立
    private static Dictionary<(Type, string), string> renames;

    [HarmonyPostfix]
    public static void Postfix(Type defType, ref string __result)
    {
        if (__result == null || !TryGetRenames(out Dictionary<(Type, string), string> map))
        {
            return;
        }
        if (!map.TryGetValue((defType, __result), out string newName))
        {
            return;
        }
        // 舊名稱仍是有效 Def 時不轉換, 避免誤導向
        if (GenDefDatabase.GetDefSilentFail(defType, __result, specialCaseForSoundDefs: false) != null)
        {
            return;
        }
        __result = newName;
    }

    private static bool TryGetRenames(out Dictionary<(Type, string), string> map)
    {
        if (renames == null)
        {
            List<DefRenameDef> defs = DefDatabase<DefRenameDef>.AllDefsListForReading;
            if (defs.Count == 0)
            {
                map = null;
                return false;
            }
            renames = new Dictionary<(Type, string), string>();
            foreach (DefRenameDef def in defs)
            {
                foreach (DefRename rename in def.renames)
                {
                    if (rename.defType != null && !rename.oldDefName.NullOrEmpty() && !rename.newDefName.NullOrEmpty())
                    {
                        renames[(rename.defType, rename.oldDefName)] = rename.newDefName;
                    }
                }
            }
        }
        map = renames;
        return true;
    }
}
