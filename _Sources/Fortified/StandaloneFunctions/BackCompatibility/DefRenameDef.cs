using System;
using System.Collections.Generic;
using Verse;

namespace Fortified;

/// <summary>
/// 宣告更名過的 Def 與型別, 讓舊存檔 / 舊翻譯鍵在讀取時自動轉成新名稱。
/// Def 由 Patch_BackCompatibility_BackCompatibleDefName 套用, 型別由 Patch_BackCompatibility_GetBackCompatibleType(Direct) 套用。
/// 各模組自行在 XML 中定義, 例:
/// <code>
/// &lt;Fortified.DefRenameDef&gt;
///   &lt;defName&gt;DMS_DefRenames&lt;/defName&gt;
///   &lt;renames&gt;
///     &lt;li&gt;
///       &lt;defType&gt;ResearchProjectDef&lt;/defType&gt;
///       &lt;oldDefName&gt;DMS_Aerospace&lt;/oldDefName&gt;
///       &lt;newDefName&gt;DMS_AdvancedControl&lt;/newDefName&gt;
///     &lt;/li&gt;
///   &lt;/renames&gt;
///   &lt;typeRenames&gt;
///     &lt;li&gt;
///       &lt;oldTypeName&gt;DMSE.WorldObject_TransferShip&lt;/oldTypeName&gt;
///       &lt;newType&gt;Fortified.WorldObject_TransferShip&lt;/newType&gt;
///     &lt;/li&gt;
///   &lt;/typeRenames&gt;
/// &lt;/Fortified.DefRenameDef&gt;
/// </code>
/// 型別對照只對存檔生效 (Class 屬性與 Type 欄位)。Def XML 的 Class 屬性在 DefRenameDef 載入前就已解析,
/// 必須直接改成新名稱。每一筆都要直接指向最終型別, 對照不會串接 (A→B→C 要寫成 A→C 與 B→C)。
/// </summary>
public class DefRenameDef : Def
{
    public List<DefRename> renames = new List<DefRename>();

    public List<TypeRename> typeRenames = new List<TypeRename>();

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors())
        {
            yield return error;
        }
        foreach (DefRename rename in renames)
        {
            if (rename.defType == null || !typeof(Def).IsAssignableFrom(rename.defType))
            {
                yield return $"rename {rename.oldDefName} -> {rename.newDefName} has missing or non-Def defType";
            }
            if (rename.oldDefName.NullOrEmpty() || rename.newDefName.NullOrEmpty())
            {
                yield return "rename has empty oldDefName or newDefName";
            }
        }
        foreach (TypeRename rename in typeRenames)
        {
            if (rename.oldTypeName.NullOrEmpty())
            {
                yield return $"type rename to {rename.newType} has empty oldTypeName";
            }
            if (rename.newType == null)
            {
                yield return $"type rename from {rename.oldTypeName} has missing newType";
            }
            else if (rename.newType.FullName == rename.oldTypeName)
            {
                yield return $"type rename {rename.oldTypeName} points to itself";
            }
        }
    }
}

public class DefRename
{
    public Type defType;
    public string oldDefName;
    public string newDefName;
}

public class TypeRename
{
    /// <summary>舊型別的完整名稱 (含 namespace), 與存檔 Class 屬性寫的相同。</summary>
    public string oldTypeName;

    public Type newType;
}
