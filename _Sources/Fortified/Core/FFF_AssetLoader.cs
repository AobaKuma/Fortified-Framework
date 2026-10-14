using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace Fortified;
// 资产加载器
[StaticConstructorOnStartup]
public static class FFF_AssetLoader
{
    private static Shader paintShader;
    private const string PaintBundleName = "fortified_shaders";

    // 涂装着色器
    // 切换语言等完整重载会以 Unload(true) 卸载所有 AssetBundle，已取得的着色器随之销毁，而静态构造不会重跑。
    // 销毁后 C# 引用仍非 null，调用端的 ?? 不会回退，材质会拿到无效着色器（Matfrom with null shader）。
    // 因此在此检测并从重新载入的 bundle 再取一次。
    public static Shader PaintShader
    {
        get
        {
            if (paintShader is not null && paintShader == null) Init();
            return paintShader;
        }
    }

    static FFF_AssetLoader()
    {
        LongEventHandler.ExecuteWhenFinished(Init);
    }

    // 执行初始化
    private static void Init()
    {
        try
        {
            paintShader = LoadShaderFromModAssets();
            if (paintShader == null)
            {
                paintShader = Shader.Find("Fortified/StandardPaintable");
            }

            if (paintShader != null)
            {
                Log.Message($"[Fortified] 涂装着色器加载成功 {paintShader.name}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[Fortified] 资产加载异常 {ex}");
        }

        // 统一为真正的 null，避免 PaintShader 每次读取都重试
        if (paintShader == null) paintShader = null;
    }

    // 从模组资源包加载着色器
    private static Shader LoadShaderFromModAssets()
    {
        Shader fallback = null;
        foreach (ModContentPack mod in LoadedModManager.RunningModsListForReading)
        {
            if (!mod.PackageId.ToLower().Contains("aoba.framework")) continue;
            if (mod.assetBundles?.loadedAssetBundles == null) continue;

            foreach (AssetBundle bundle in mod.assetBundles.loadedAssetBundles)
            {
                if (!IsPaintBundle(bundle)) continue;

                var shader = LoadShader(bundle);
                if (shader == null) continue;

                if (bundle.name == PaintBundleName + CurrentBundleSuffix()) return shader;
                fallback ??= shader;
            }
        }
        return fallback;
    }

    private static bool IsPaintBundle(AssetBundle bundle)
    {
        if (bundle == null) return false;

        string name = bundle.name;
        return name == PaintBundleName || name == PaintBundleName + CurrentBundleSuffix();
    }

    private static Shader LoadShader(AssetBundle bundle)
    {
        var shader = bundle.LoadAsset<Shader>("Assets/Shaders/FF_StandardPaintable.shader");
        return shader ?? bundle.LoadAllAssets<Shader>().FirstOrDefault();
    }

    private static string CurrentBundleSuffix()
    {
        switch (Application.platform)
        {
            case RuntimePlatform.LinuxPlayer:
            case RuntimePlatform.LinuxEditor:
                return "_linux";
            case RuntimePlatform.OSXPlayer:
            case RuntimePlatform.OSXEditor:
                return "_mac";
            default:
                return "_win";
        }
    }
}
