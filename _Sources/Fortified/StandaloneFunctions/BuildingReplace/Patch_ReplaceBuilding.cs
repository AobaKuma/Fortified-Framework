using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 攔截 <c>GenSpawn.Spawn</c>，把掛有 <see cref="ModExtension_ReplaceBuilding"/> 的建築
    /// 依機率替換成另一個 ThingDef。所有 GenSpawn.Spawn 多載最後都會走進這個 7 參數版本。
    /// <para>
    /// 效能：非地圖生成期間會在第一個 static 欄位比較就返回，不做任何查表。
    /// </para>
    /// <para>
    /// 限制：呼叫端若在 Spawn 之後仍持有自己建立的那個 Thing 參考（而不是用 Spawn 的回傳值），
    /// 拿到的會是沒被生成的原物件。GenSpawn.Spawn 會回傳被換過的新物件。
    /// </para>
    /// </summary>
    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.Spawn), new Type[]
    {
        typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool)
    })]
    public static class Patch_ReplaceBuilding
    {
        private static readonly Dictionary<ThingDef, ModExtension_ReplaceBuilding> Empty =
            new Dictionary<ThingDef, ModExtension_ReplaceBuilding>();

        private static Dictionary<ThingDef, ModExtension_ReplaceBuilding> cache;
        private static bool anyOutsideMapGen;

        /// <summary>defName → 擴充 的查表快取（Def 尚未載入時回傳空表）。</summary>
        public static Dictionary<ThingDef, ModExtension_ReplaceBuilding> Cache
        {
            get
            {
                EnsureCache();
                return cache ?? Empty;
            }
        }

        /// <summary>是否存在把 onlyDuringMapGen 設為 false 的 def；沒有的話地圖生成外可完全跳過。</summary>
        private static bool AnyOutsideMapGen
        {
            get
            {
                EnsureCache();
                return anyOutsideMapGen;
            }
        }

        private static void EnsureCache()
        {
            if (cache == null) RebuildCache();
        }

        /// <summary>掃描 DefDatabase 重建快取。Def 尚未載入時不留下空快取，之後會再試一次。</summary>
        public static void RebuildCache()
        {
            try
            {
                List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
                if (allDefs == null || allDefs.Count == 0) return;

                var built = new Dictionary<ThingDef, ModExtension_ReplaceBuilding>();
                bool outside = false;

                for (int i = 0; i < allDefs.Count; i++)
                {
                    ThingDef def = allDefs[i];
                    if (def?.modExtensions == null) continue;

                    var ext = def.GetModExtension<ModExtension_ReplaceBuilding>();
                    if (ext == null) continue;

                    built[def] = ext;
                    if (!ext.onlyDuringMapGen) outside = true;
                }

                cache = built;
                anyOutsideMapGen = outside;
            }
            catch (Exception e)
            {
                Log.Error($"[ReplaceBuilding] 建立快取失敗，本功能停用：{e}");
                cache = Empty;
                anyOutsideMapGen = false;
            }
        }

        [HarmonyPrefix]
        public static void Prefix(ref Thing newThing, Map map, ref Rot4 rot, bool respawningAfterLoad)
        {
            try
            {
                if (newThing == null || map == null || respawningAfterLoad) return;

                bool duringMapGen = MapGenerator.mapBeingGenerated == map;
                // 非地圖生成期間、且沒有任何 def 想在生成期外作用 → 直接離開（每次 Spawn 的常見路徑）
                if (!duringMapGen && !AnyOutsideMapGen) return;

                ThingDef def = newThing.def;
                if (def == null || newThing.Spawned || newThing.Destroyed) return;
                if (newThing is Blueprint || newThing is Frame || newThing is MinifiedThing) return;

                if (!Cache.TryGetValue(def, out ModExtension_ReplaceBuilding ext) || ext == null) return;
                if (ext.onlyDuringMapGen && !duringMapGen) return;
                if (!ext.HasAnyValidOption) return;

                if (!ext.TryPickReplacement(def, out ModExtension_ReplaceBuilding.Option option)) return;

                Thing replacement = ReplaceBuildingUtility.TryMakeReplacement(newThing, ext, option);
                if (replacement == null) return;

                // 新 def 不可旋轉時把朝向正規化，避免帶著奇怪的 Rotation 進 GenSpawn
                if (!replacement.def.rotatable && rot != Rot4.North) rot = Rot4.North;

                if (Prefs.DevMode)
                    Log.Message($"[ReplaceBuilding] {def.defName} → {replacement.def.defName} (stuff={replacement.Stuff?.defName ?? "-"}, mapGen={duringMapGen})");

                newThing = replacement;
            }
            catch (Exception e)
            {
                Log.Error($"[ReplaceBuilding] Prefix 發生例外，保留原建築：{e}");
            }
        }
    }
}
