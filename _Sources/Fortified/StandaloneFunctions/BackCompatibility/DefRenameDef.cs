using System;
using System.Collections.Generic;
using Verse;

namespace Fortified;

/// <summary>
/// 宣告更名過的 Def, 讓舊存檔 / 舊翻譯鍵在讀取時自動轉成新名稱 (由 Patch_BackCompatibility_BackCompatibleDefName 套用)。
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
/// &lt;/Fortified.DefRenameDef&gt;
/// </code>
/// </summary>
public class DefRenameDef : Def
{
    public List<DefRename> renames = new List<DefRename>();

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
    }
}

public class DefRename
{
    public Type defType;
    public string oldDefName;
    public string newDefName;
}
