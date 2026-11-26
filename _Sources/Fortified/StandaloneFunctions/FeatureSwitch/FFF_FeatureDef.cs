using System;
using System.Collections.Generic;
using Verse;

namespace Fortified;

/// <summary>
/// 一項可開關的 FFF 功能。沒有任何模組用到時, 它的 Harmony 補丁不掛、標了 <see cref="FFF_FeatureAttribute"/> 的
/// 地圖／世界／遊戲元件不建立。開關在啟動時決定 (<see cref="FFF_FeatureRegistry.Resolve"/>), 遊戲中不能切換。
/// <code>
/// &lt;Fortified.FFF_FeatureDef&gt;
///   &lt;defName&gt;FFF_Feature_SeaPlatform&lt;/defName&gt;
///   &lt;label&gt;sea platform&lt;/label&gt;
///   &lt;harmonyCategory&gt;FFF.SeaPlatform&lt;/harmonyCategory&gt;
///   &lt;autoEnableIfDefUses&gt;
///     &lt;li&gt;Fortified.SeaPlatformMapExtension&lt;/li&gt;
///   &lt;/autoEnableIfDefUses&gt;
/// &lt;/Fortified.FFF_FeatureDef&gt;
/// </code>
/// </summary>
public class FFF_FeatureDef : Def
{
    /// <summary>對應補丁類別上的 [HarmonyPatchCategory]; 空 = 這項功能沒有補丁。</summary>
    public string harmonyCategory;

    /// <summary>啟用這項功能時一併啟用的功能。</summary>
    public List<FFF_FeatureDef> dependsOn = new List<FFF_FeatureDef>();

    /// <summary>
    /// 任何非 FFF 本身的 Def 用到這些型別 (或其子類別) 時自動啟用: Def 本身的類別、modExtensions、
    /// ThingDef 的 thingClass 與 comps、WorldObjectDef 的 worldObjectClass 與 comps、GenStepDef 的 genStep、
    /// SitePartDef／IncidentDef 的 workerClass、MapGeneratorDef 的 customMapComponents。
    /// </summary>
    public List<Type> autoEnableIfDefUses = new List<Type>();

    /// <summary>需要的模組或 DLC packageId, 全部啟用時才可能打開 (例: ludeon.rimworld.odyssey)。</summary>
    public List<string> requiredModIds = new List<string>();

    /// <summary>不需要任何模組宣告也預設啟用。給既有、原本就無條件生效的功能用。</summary>
    public bool enabledByDefault;

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors())
        {
            yield return error;
        }
        if (dependsOn.Contains(this))
        {
            yield return "feature depends on itself";
        }
        foreach (Type type in autoEnableIfDefUses)
        {
            if (type == null)
            {
                yield return "autoEnableIfDefUses has a null type";
            }
        }
    }
}

/// <summary>
/// 使用者模組宣告要用哪些 FFF 功能。純 C# 使用 (沒有 Def 引用功能型別) 的模組一定要寫,
/// 否則 <see cref="FFF_FeatureDef.autoEnableIfDefUses"/> 偵測不到。
/// <code>
/// &lt;Fortified.FFF_FeatureRequestDef&gt;
///   &lt;defName&gt;DMSE_FeatureRequest&lt;/defName&gt;
///   &lt;features&gt;
///     &lt;li&gt;FFF_Feature_SeaPlatform&lt;/li&gt;
///   &lt;/features&gt;
/// &lt;/Fortified.FFF_FeatureRequestDef&gt;
/// </code>
/// </summary>
public class FFF_FeatureRequestDef : Def
{
    public List<FFF_FeatureDef> features = new List<FFF_FeatureDef>();
}

/// <summary>
/// 標在 MapComponent／WorldComponent／GameComponent 上: 功能未啟用時不建立這個元件
/// (由 Patch_FFF_FeatureComponents 在 FillComponents 之後移除)。
/// 讀到含有這種元件資料的存檔時, 資料會被丟掉; 功能未啟用通常代表相依模組已移除, 這是預期行為。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class FFF_FeatureAttribute : Attribute
{
    public readonly string featureDefName;

    public FFF_FeatureAttribute(string featureDefName)
    {
        this.featureDefName = featureDefName;
    }
}
