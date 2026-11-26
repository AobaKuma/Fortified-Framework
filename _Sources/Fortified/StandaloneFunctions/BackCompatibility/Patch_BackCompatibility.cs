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

/// <summary>
/// 依 DefRenameDef.typeRenames 把存檔中找不到的舊型別名稱轉成新型別。
/// 只在原版找不到型別時查表, 所以不會蓋掉仍存在的型別; 平常讀檔只多一次 null 判斷。
/// </summary>
public static class TypeRenameUtility
{
    // 舊型別完整名稱 -> 新型別; DefRenameDef 載入進 DefDatabase 後才建立。
    // Def XML 解析期間 DefDatabase 還是空的, 此時不建立快取, 以免存下空表。
    private static Dictionary<string, Type> typeRenames;

    public static Type Resolve(Type baseType, string oldTypeName)
    {
        if (oldTypeName.NullOrEmpty() || !TryGetTypeRenames(out Dictionary<string, Type> map))
        {
            return null;
        }
        if (!map.TryGetValue(oldTypeName, out Type newType))
        {
            return null;
        }
        // ParseType 傳入的 baseType 是 typeof(Type), 不代表欄位的實際型別, 不做檢查
        if (baseType != null && baseType != typeof(Type) && !baseType.IsAssignableFrom(newType))
        {
            Log.ErrorOnce($"[Fortified] DefRenameDef maps {oldTypeName} to {newType}, which is not a {baseType}; ignored.",
                ("FFF_TypeRename" + oldTypeName).GetHashCode());
            return null;
        }
        return newType;
    }

    private static bool TryGetTypeRenames(out Dictionary<string, Type> map)
    {
        if (typeRenames == null)
        {
            List<DefRenameDef> defs = DefDatabase<DefRenameDef>.AllDefsListForReading;
            if (defs.Count == 0)
            {
                map = null;
                return false;
            }
            typeRenames = new Dictionary<string, Type>();
            foreach (DefRenameDef def in defs)
            {
                foreach (TypeRename rename in def.typeRenames)
                {
                    if (!rename.oldTypeName.NullOrEmpty() && rename.newType != null)
                    {
                        typeRenames[rename.oldTypeName] = rename.newType;
                    }
                }
            }
        }
        map = typeRenames;
        return map.Count > 0;
    }
}

/// <summary>
/// 存檔中 IExposable、Thing、元件、世界物件的 Class 屬性 (ScribeExtractor.SaveableFromNode)。
/// 同樣用 postfix: 環境相同時原版會跳過轉換鏈, 直接回傳 GenTypes.GetTypeInAnyAssembly 的結果。
/// </summary>
[HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.GetBackCompatibleType))]
public static class Patch_BackCompatibility_GetBackCompatibleType
{
    [HarmonyPostfix]
    public static void Postfix(Type baseType, string providedClassName, ref Type __result)
    {
        if (__result == null)
        {
            __result = TypeRenameUtility.Resolve(baseType, providedClassName);
        }
    }
}

/// <summary>存檔中的 Type 欄位 (ParseHelper.ParseType 找不到型別時呼叫)。</summary>
[HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.GetBackCompatibleTypeDirect))]
public static class Patch_BackCompatibility_GetBackCompatibleTypeDirect
{
    [HarmonyPostfix]
    public static void Postfix(Type baseType, string providedClassName, ref Type __result)
    {
        if (__result == null)
        {
            __result = TypeRenameUtility.Resolve(baseType, providedClassName);
        }
    }
}
