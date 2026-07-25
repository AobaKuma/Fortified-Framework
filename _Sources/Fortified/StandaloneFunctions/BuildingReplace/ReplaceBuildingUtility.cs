using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace Fortified
{
    /// <summary>
    /// <see cref="ModExtension_ReplaceBuilding"/> 的共用邏輯：材質判定、建立替換物、屬性沿用。
    /// 全部方法都以「失敗就回傳 null / 不動原物」為原則，不對外丟例外。
    /// </summary>
    public static class ReplaceBuildingUtility
    {
        /// <summary>該材質是否符合目標 def 的 stuffCategories。</summary>
        public static bool StuffAllowedFor(ThingDef stuff, ThingDef def)
        {
            if (stuff == null || def == null || !def.MadeFromStuff) return false;
            List<StuffCategoryDef> cats = stuff.stuffProps?.categories;
            if (cats.NullOrEmpty() || def.stuffCategories.NullOrEmpty()) return false;

            for (int i = 0; i < def.stuffCategories.Count; i++)
                if (cats.Contains(def.stuffCategories[i]))
                    return true;
            return false;
        }

        /// <summary>依候選設定與原建築挑出要用的材質；目標不吃材質時回傳 null。</summary>
        public static ThingDef ResolveStuff(ModExtension_ReplaceBuilding ext, ModExtension_ReplaceBuilding.Option option, Thing original)
        {
            ThingDef target = option?.thing;
            if (target == null || !target.MadeFromStuff) return null;

            if (StuffAllowedFor(option.stuff, target)) return option.stuff;
            if (ext != null && ext.inheritStuff && StuffAllowedFor(original?.Stuff, target)) return original.Stuff;

            ThingDef fallback = GenStuff.DefaultStuffFor(target);
            if (fallback != null) return fallback;

            // 極端狀況：def 宣稱吃材質卻找不到任何合法材質，交由呼叫端放棄替換。
            return null;
        }

        /// <summary>
        /// 依設定建立替換用的 Thing。任何一步失敗都回傳 null，呼叫端應保留原本的建築。
        /// </summary>
        public static Thing TryMakeReplacement(Thing original, ModExtension_ReplaceBuilding ext, ModExtension_ReplaceBuilding.Option option)
        {
            if (original == null || ext == null || option?.thing == null) return null;

            ThingDef target = option.thing;
            try
            {
                ThingDef stuff = ResolveStuff(ext, option, original);
                if (target.MadeFromStuff && stuff == null)
                {
                    Log.ErrorOnce($"[ReplaceBuilding] {target.defName} 需要材質但找不到任何合法材質，取消替換。", target.GetHashCode() ^ 0x5B1D);
                    return null;
                }

                Thing replacement = ThingMaker.MakeThing(target, stuff);
                if (replacement == null)
                {
                    Log.ErrorOnce($"[ReplaceBuilding] ThingMaker 無法建立 {target.defName}，取消替換。", target.GetHashCode() ^ 0x5B2E);
                    return null;
                }

                if (ext.inheritFaction && original.Faction != null)
                    replacement.SetFactionDirect(original.Faction);

                if (ext.inheritHitPointsPercent)
                    CopyHitPointsPercent(original, replacement);

                return replacement;
            }
            catch (Exception e)
            {
                Log.Error($"[ReplaceBuilding] 建立 {target.defName} 取代 {original.def?.defName} 時發生例外，保留原建築：{e}");
                return null;
            }
        }

        /// <summary>把原建築的血量百分比套到新建築；任一方不使用血量就跳過。</summary>
        public static void CopyHitPointsPercent(Thing original, Thing replacement)
        {
            if (original?.def == null || replacement?.def == null) return;
            if (!original.def.useHitPoints || !replacement.def.useHitPoints) return;

            int originalMax = original.MaxHitPoints;
            int replacementMax = replacement.MaxHitPoints;
            if (originalMax <= 0 || replacementMax <= 0) return;

            float pct = Mathf.Clamp01((float)original.HitPoints / originalMax);
            replacement.HitPoints = Mathf.Clamp(Mathf.RoundToInt(replacementMax * pct), 1, replacementMax);
        }
    }

    /// <summary>
    /// 啟動時掃過所有掛了 <see cref="ModExtension_ReplaceBuilding"/> 的 ThingDef 並輸出設定問題，
    /// 順便把 <see cref="Patch_ReplaceBuilding"/> 的查表快取建好。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ReplaceBuildingValidator
    {
        static ReplaceBuildingValidator()
        {
            try
            {
                Patch_ReplaceBuilding.RebuildCache();

                foreach (KeyValuePair<ThingDef, ModExtension_ReplaceBuilding> pair in Patch_ReplaceBuilding.Cache)
                    foreach (string error in pair.Value.ValidationErrors(pair.Key))
                        Log.Warning(error);
            }
            catch (Exception e)
            {
                Log.Error($"[ReplaceBuilding] 設定檢查失敗：{e}");
            }
        }
    }
}
