using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Fortified;

/// <summary>
/// 啟動時決定哪些 <see cref="FFF_FeatureDef"/> 啟用, 並只掛已啟用功能的 Harmony 補丁。
/// 由 <see cref="HarmonyEntry"/> 在 Def 載入完成後呼叫一次。玩家不能手動開關, 設定頁只列出每項功能由哪些模組啟用。
/// </summary>
public static class FFF_FeatureRegistry
{
    private static readonly HashSet<string> enabled = new HashSet<string>();

    // 功能 defName -> 啟用它的模組 (每個模組一筆, 依發現順序)
    private static readonly Dictionary<string, List<FFF_FeatureEnabler>> enablers = new Dictionary<string, List<FFF_FeatureEnabler>>();

    // 有模組要用、卻因缺少必要模組或依賴而保持關閉的功能 -> 原因
    private static readonly Dictionary<string, string> blocked = new Dictionary<string, string>();

    // 標了 FFF_FeatureAttribute 的元件型別 -> 功能 defName; 沒有任何標記時 FillComponents 補丁直接返回
    private static Dictionary<Type, string> gatedComponents;

    private static readonly Dictionary<Type, bool> componentAllowedCache = new Dictionary<Type, bool>();

    private static readonly List<FFF_FeatureEnabler> None = new List<FFF_FeatureEnabler>();

    public static bool Resolved { get; private set; }

    public static IEnumerable<FFF_FeatureDef> Enabled =>
        DefDatabase<FFF_FeatureDef>.AllDefsListForReading.Where(f => enabled.Contains(f.defName));

    public static bool IsEnabled(FFF_FeatureDef feature) => feature != null && enabled.Contains(feature.defName);

    public static bool IsEnabled(string featureDefName) => featureDefName != null && enabled.Contains(featureDefName);

    /// <summary>啟用這項功能的模組 (功能關閉時為空)。</summary>
    public static IReadOnlyList<FFF_FeatureEnabler> EnablersOf(string featureDefName) =>
        featureDefName != null && enabled.Contains(featureDefName) && enablers.TryGetValue(featureDefName, out List<FFF_FeatureEnabler> list)
            ? list
            : None;

    /// <summary>有模組要用、卻保持關閉的原因 (例: 缺少 DLC); 沒有則為 null。</summary>
    public static string BlockedReason(string featureDefName) =>
        featureDefName != null && blocked.TryGetValue(featureDefName, out string reason) ? reason : null;

    /// <summary>決定啟用的功能, 並掛上它們的補丁分類。只能呼叫一次。</summary>
    public static void Resolve(Harmony harmony)
    {
        if (Resolved)
        {
            return;
        }
        Resolved = true;

        List<FFF_FeatureDef> features = DefDatabase<FFF_FeatureDef>.AllDefsListForReading;
        if (features.Count > 0)
        {
            ModContentPack own = LoadedModManager.GetMod<FortifiedMod>()?.Content;
            foreach (FFF_FeatureDef feature in features)
            {
                if (feature.enabledByDefault)
                {
                    AddEnabler(feature, feature.modContentPack ?? own, "enabled by default");
                }
            }
            foreach (FFF_FeatureRequestDef request in DefDatabase<FFF_FeatureRequestDef>.AllDefsListForReading)
            {
                foreach (FFF_FeatureDef feature in request.features)
                {
                    AddEnabler(feature, request.modContentPack, "FFF_FeatureRequestDef " + request.defName);
                }
            }
            DetectDefUsage(features, own);
            ExpandDependencies(features);
            DropMissingMods(features);
            ApplyHarmonyCategories(harmony, features);
            LogSummary(features);
        }
        BuildGatedComponents();
    }

    private static void AddEnabler(FFF_FeatureDef feature, ModContentPack mod, string detail)
    {
        if (feature == null)
        {
            return;
        }
        enabled.Add(feature.defName);
        if (!enablers.TryGetValue(feature.defName, out List<FFF_FeatureEnabler> list))
        {
            list = new List<FFF_FeatureEnabler>();
            enablers[feature.defName] = list;
        }
        string modName = mod?.Name ?? "?";
        if (list.Any(e => e.modName == modName))
        {
            return;
        }
        list.Add(new FFF_FeatureEnabler { modName = modName, packageId = mod?.PackageIdPlayerFacing, detail = detail });
    }

    // 掃描非 FFF 本身的 Def, 找出每個模組用到功能型別的地方。FFF 自己附帶的框架 Def 不算, 否則功能永遠會被打開。
    // 每個 (功能, 模組) 只記第一個找到的 Def。
    private static void DetectDefUsage(List<FFF_FeatureDef> features, ModContentPack own)
    {
        List<FFF_FeatureDef> candidates = features.Where(f => f.autoEnableIfDefUses.Count > 0).ToList();
        if (candidates.Count == 0)
        {
            return;
        }
        HashSet<(string, ModContentPack)> found = new HashSet<(string, ModContentPack)>();
        foreach (Type defType in GenDefDatabase.AllDefTypesWithDatabases())
        {
            foreach (Def def in GenDefDatabase.GetAllDefsInDatabaseForDef(defType))
            {
                ModContentPack mod = def.modContentPack;
                if (mod == own || mod == null)
                {
                    continue;
                }
                foreach (Type used in TypesUsedBy(def))
                {
                    foreach (FFF_FeatureDef feature in candidates)
                    {
                        if (found.Contains((feature.defName, mod)) || !feature.autoEnableIfDefUses.Any(t => t != null && t.IsAssignableFrom(used)))
                        {
                            continue;
                        }
                        found.Add((feature.defName, mod));
                        AddEnabler(feature, mod, $"{def.defName} uses {used.Name}");
                    }
                }
            }
        }
    }

    private static IEnumerable<Type> TypesUsedBy(Def def)
    {
        yield return def.GetType();
        if (def.modExtensions != null)
        {
            foreach (DefModExtension extension in def.modExtensions)
            {
                if (extension != null)
                {
                    yield return extension.GetType();
                }
            }
        }
        switch (def)
        {
            case ThingDef thingDef:
                if (thingDef.thingClass != null)
                {
                    yield return thingDef.thingClass;
                }
                if (thingDef.comps != null)
                {
                    foreach (CompProperties comp in thingDef.comps)
                    {
                        if (comp == null)
                        {
                            continue;
                        }
                        yield return comp.GetType();
                        if (comp.compClass != null)
                        {
                            yield return comp.compClass;
                        }
                    }
                }
                break;
            case WorldObjectDef worldObjectDef:
                if (worldObjectDef.worldObjectClass != null)
                {
                    yield return worldObjectDef.worldObjectClass;
                }
                if (worldObjectDef.comps != null)
                {
                    foreach (WorldObjectCompProperties comp in worldObjectDef.comps)
                    {
                        if (comp == null)
                        {
                            continue;
                        }
                        yield return comp.GetType();
                        if (comp.compClass != null)
                        {
                            yield return comp.compClass;
                        }
                    }
                }
                break;
            case GenStepDef genStepDef:
                if (genStepDef.genStep != null)
                {
                    yield return genStepDef.genStep.GetType();
                }
                break;
            case SitePartDef sitePartDef:
                if (sitePartDef.workerClass != null)
                {
                    yield return sitePartDef.workerClass;
                }
                break;
            case IncidentDef incidentDef:
                if (incidentDef.workerClass != null)
                {
                    yield return incidentDef.workerClass;
                }
                break;
            case MapGeneratorDef mapGeneratorDef:
                if (mapGeneratorDef.customMapComponents != null)
                {
                    foreach (Type component in mapGeneratorDef.customMapComponents)
                    {
                        if (component != null)
                        {
                            yield return component;
                        }
                    }
                }
                break;
        }
    }

    // 依賴的功能一起啟用; 啟用它的模組也記在依賴上 (經由哪項功能)
    private static void ExpandDependencies(List<FFF_FeatureDef> features)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (FFF_FeatureDef feature in features)
            {
                if (!enabled.Contains(feature.defName) || !enablers.TryGetValue(feature.defName, out List<FFF_FeatureEnabler> sources))
                {
                    continue;
                }
                foreach (FFF_FeatureDef dependency in feature.dependsOn)
                {
                    if (dependency == null)
                    {
                        continue;
                    }
                    foreach (FFF_FeatureEnabler source in sources.ToList())
                    {
                        if (!enablers.TryGetValue(dependency.defName, out List<FFF_FeatureEnabler> existing))
                        {
                            existing = new List<FFF_FeatureEnabler>();
                            enablers[dependency.defName] = existing;
                        }
                        if (existing.Any(e => e.modName == source.modName))
                        {
                            continue;
                        }
                        enabled.Add(dependency.defName);
                        existing.Add(new FFF_FeatureEnabler
                        {
                            modName = source.modName,
                            packageId = source.packageId,
                            detail = "required by " + feature.defName,
                            via = feature
                        });
                        changed = true;
                    }
                }
            }
        }
    }

    // 缺少必要模組 (例: DLC) 的功能關閉, 依賴它的功能也一起關閉
    private static void DropMissingMods(List<FFF_FeatureDef> features)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (FFF_FeatureDef feature in features)
            {
                if (!enabled.Contains(feature.defName))
                {
                    continue;
                }
                string missing = feature.requiredModIds.FirstOrDefault(id => !id.NullOrEmpty() && !ModsConfig.IsActive(id));
                FFF_FeatureDef disabledDependency = missing == null
                    ? feature.dependsOn.FirstOrDefault(d => d != null && !enabled.Contains(d.defName))
                    : null;
                if (missing == null && disabledDependency == null)
                {
                    continue;
                }
                string why = missing != null ? "missing mod " + missing : "dependency " + disabledDependency.defName + " is off";
                Log.Warning($"[Fortified] Feature {feature.defName} is used by {ModNames(feature.defName)} but stays off: {why}.");
                enabled.Remove(feature.defName);
                blocked[feature.defName] = why;
                changed = true;
            }
        }
    }

    private static string ModNames(string featureDefName) =>
        enablers.TryGetValue(featureDefName, out List<FFF_FeatureEnabler> list) && list.Count > 0
            ? string.Join(", ", list.Select(e => e.modName))
            : "-";

    private static void ApplyHarmonyCategories(Harmony harmony, List<FFF_FeatureDef> features)
    {
        foreach (FFF_FeatureDef feature in features)
        {
            if (!enabled.Contains(feature.defName) || feature.harmonyCategory.NullOrEmpty())
            {
                continue;
            }
            try
            {
                harmony.PatchCategory(feature.harmonyCategory);
            }
            catch (Exception ex)
            {
                Log.Error($"[Fortified] Patching category {feature.harmonyCategory} for feature {feature.defName} failed: {ex}");
            }
        }
    }

    private static void LogSummary(List<FFF_FeatureDef> features)
    {
        StringBuilder sb = new StringBuilder("[Fortified] Features: ");
        sb.Append(enabled.Count).Append('/').Append(features.Count).Append(" enabled");
        foreach (FFF_FeatureDef feature in features)
        {
            sb.AppendLine();
            bool on = enabled.Contains(feature.defName);
            sb.Append(on ? "  + " : "  - ").Append(feature.defName);
            if (on)
            {
                sb.Append(" (").Append(string.Join("; ", EnablersOf(feature.defName).Select(e => e.modName + ": " + e.detail))).Append(')');
            }
            else if (blocked.TryGetValue(feature.defName, out string why))
            {
                sb.Append(" (blocked: ").Append(why).Append(')');
            }
        }
        Log.Message(sb.ToString());
    }

    private static void BuildGatedComponents()
    {
        gatedComponents = new Dictionary<Type, string>();
        foreach (Type baseType in new[] { typeof(MapComponent), typeof(WorldComponent), typeof(GameComponent) })
        {
            foreach (Type type in baseType.AllSubclassesNonAbstract())
            {
                FFF_FeatureAttribute attribute = type.GetCustomAttribute<FFF_FeatureAttribute>(inherit: true);
                if (attribute != null)
                {
                    gatedComponents[type] = attribute.featureDefName;
                }
            }
        }
        if (gatedComponents.Count == 0)
        {
            gatedComponents = null;
        }
    }

    /// <summary>這個元件型別是否該建立。沒有標 <see cref="FFF_FeatureAttribute"/> 的一律允許。</summary>
    public static bool ComponentAllowed(Type componentType)
    {
        if (gatedComponents == null || componentType == null)
        {
            return true;
        }
        if (!componentAllowedCache.TryGetValue(componentType, out bool allowed))
        {
            allowed = !gatedComponents.TryGetValue(componentType, out string feature) || enabled.Contains(feature);
            componentAllowedCache[componentType] = allowed;
        }
        return allowed;
    }

    internal static bool AnyGatedComponents => gatedComponents != null;
}

/// <summary>FillComponents 之後移除未啟用功能的元件。沒有任何標記的元件時只多一次判斷。</summary>
[HarmonyPatch]
public static class Patch_FFF_FeatureComponents
{
    [HarmonyPatch(typeof(Map), "FillComponents")]
    [HarmonyPostfix]
    public static void MapPostfix(List<MapComponent> ___components)
    {
        if (FFF_FeatureRegistry.AnyGatedComponents)
        {
            ___components.RemoveAll(c => c != null && !FFF_FeatureRegistry.ComponentAllowed(c.GetType()));
        }
    }

    [HarmonyPatch(typeof(World), "FillComponents")]
    [HarmonyPostfix]
    public static void WorldPostfix(List<WorldComponent> ___components)
    {
        if (FFF_FeatureRegistry.AnyGatedComponents)
        {
            ___components.RemoveAll(c => c != null && !FFF_FeatureRegistry.ComponentAllowed(c.GetType()));
        }
    }

    [HarmonyPatch(typeof(Game), "FillComponents")]
    [HarmonyPostfix]
    public static void GamePostfix(List<GameComponent> ___components)
    {
        if (FFF_FeatureRegistry.AnyGatedComponents)
        {
            ___components.RemoveAll(c => c != null && !FFF_FeatureRegistry.ComponentAllowed(c.GetType()));
        }
    }
}

/// <summary>啟用某項功能的一個模組（設定頁與 Log 顯示用）。</summary>
public class FFF_FeatureEnabler
{
    public string modName;
    public string packageId;

    /// <summary>怎麼啟用的：宣告的 FFF_FeatureRequestDef、用到功能型別的 Def，或 "required by" 另一項功能。</summary>
    public string detail;

    /// <summary>經由哪項功能的依賴而啟用；直接啟用時為 null。</summary>
    public FFF_FeatureDef via;
}
