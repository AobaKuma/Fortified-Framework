using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 掛在 ThingDef 上：這個建築生成時，有 <see cref="replaceChance"/> 的機率被替換成
    /// <see cref="replacements"/> 中依權重抽出的另一個建築。
    /// <para>實作見 <see cref="Patch_ReplaceBuilding"/>，預設只在地圖生成期間生效。</para>
    /// <para>注意：替換不會連鎖——換出來的新建築即使自己也掛了本擴充，也不會再被替換一次。</para>
    /// <example>
    /// <code>
    /// &lt;modExtensions&gt;
    ///   &lt;li Class="Fortified.ModExtension_ReplaceBuilding"&gt;
    ///     &lt;replaceChance&gt;0.35&lt;/replaceChance&gt;
    ///     &lt;replacements&gt;
    ///       &lt;li&gt;&lt;thing&gt;Fortified_RuinedTurret&lt;/thing&gt;&lt;weight&gt;2&lt;/weight&gt;&lt;/li&gt;
    ///       &lt;li&gt;&lt;thing&gt;Fortified_EmptyPlatform&lt;/thing&gt;&lt;weight&gt;1&lt;/weight&gt;&lt;stuff&gt;Steel&lt;/stuff&gt;&lt;/li&gt;
    ///     &lt;/replacements&gt;
    ///   &lt;/li&gt;
    /// &lt;/modExtensions&gt;
    /// </code>
    /// </example>
    /// </summary>
    public class ModExtension_ReplaceBuilding : DefModExtension
    {
        /// <summary>單一替換候選。</summary>
        public class Option
        {
            /// <summary>要替換成的 ThingDef，必填。</summary>
            public ThingDef thing;

            /// <summary>抽選權重，必須大於 0。</summary>
            public float weight = 1f;

            /// <summary>
            /// 指定材質。留空時：若 <see cref="inheritStuff"/> 為 true 則沿用原建築材質，
            /// 不合法再退回 <c>GenStuff.DefaultStuffFor</c>。
            /// </summary>
            public ThingDef stuff;

            public override string ToString() => $"({thing?.defName ?? "null"} w={weight})";
        }

        /// <summary>替換發生的機率（0~1）。1 = 必定從清單中換一個。</summary>
        public float replaceChance = 1f;

        /// <summary>替換候選清單。空清單等於停用。</summary>
        public List<Option> replacements = new List<Option>();

        /// <summary>true（預設）時只在該地圖正在生成（<c>MapGenerator.mapBeingGenerated</c>）時作用，玩家自己蓋的、搬移的、藍圖完成的建築都不受影響。</summary>
        public bool onlyDuringMapGen = true;

        /// <summary>true（預設）時只接受與原建築 <c>def.size</c> 相同的候選，避免佔位不同造成重疊或卡住通道。</summary>
        public bool requireSameSize = true;

        /// <summary>沿用原建築的派系。</summary>
        public bool inheritFaction = true;

        /// <summary>沿用原建築的材質（僅在新舊 def 都吃材質且材質類別相容時）。</summary>
        public bool inheritStuff = true;

        /// <summary>把原建築的血量百分比套到新建築上。</summary>
        public bool inheritHitPointsPercent = true;

        // ── 快取 ─────────────────────────────────────────────
        private List<Option> validCache;
        private ThingDef poolCachedFor;
        private List<Option> poolCache;

        /// <summary>過濾掉 null / 權重非正的候選。</summary>
        public List<Option> ValidOptions =>
            validCache ??= replacements == null
                ? new List<Option>()
                : replacements.Where(o => o?.thing != null && o.weight > 0f).ToList();

        public bool HasAnyValidOption => ValidOptions.Count > 0;

        /// <summary>
        /// 依 <see cref="requireSameSize"/> 過濾候選後擲骰，決定是否替換以及換成什麼。
        /// 不合法設定一律回傳 false（不替換），不擲骰、不丟例外。
        /// </summary>
        public bool TryPickReplacement(ThingDef original, out Option picked)
        {
            picked = null;

            List<Option> pool = ValidOptions;
            if (pool.Count == 0) return false;

            if (requireSameSize && original != null)
            {
                if (poolCachedFor != original || poolCache == null)
                {
                    poolCachedFor = original;
                    poolCache = pool.Where(o => o.thing.size == original.size).ToList();
                }
                pool = poolCache;
                if (pool.Count == 0) return false;
            }

            if (replaceChance < 1f && !Rand.Chance(replaceChance)) return false;

            picked = pool.RandomElementByWeight(o => o.weight);
            return picked?.thing != null && picked.thing != original;
        }

        /// <summary>設定檢查，由 <see cref="ReplaceBuildingValidator"/> 在遊戲啟動時統一輸出。</summary>
        public IEnumerable<string> ValidationErrors(ThingDef owner)
        {
            string tag = $"[ReplaceBuilding] {owner?.defName ?? "???"}";

            if (replaceChance <= 0f)
                yield return $"{tag}: replaceChance = {replaceChance}，永遠不會替換。";
            else if (replaceChance > 1f)
                yield return $"{tag}: replaceChance = {replaceChance} 超過 1，將視為必定替換。";

            if (replacements.NullOrEmpty())
            {
                yield return $"{tag}: replacements 為空，此擴充不會有任何作用。";
                yield break;
            }

            for (int i = 0; i < replacements.Count; i++)
            {
                Option o = replacements[i];
                if (o == null)
                {
                    yield return $"{tag}: replacements[{i}] 為 null。";
                    continue;
                }
                if (o.thing == null)
                {
                    yield return $"{tag}: replacements[{i}].thing 未設定，或 defName 找不到對應的 ThingDef。";
                    continue;
                }
                if (o.weight <= 0f)
                    yield return $"{tag}: replacements[{i}] ({o.thing.defName}) 的 weight 為 {o.weight}，必須大於 0，該項會被忽略。";
                if (o.stuff != null && !o.thing.MadeFromStuff)
                    yield return $"{tag}: replacements[{i}] 指定了 stuff，但 {o.thing.defName} 不是可選材質的建築，該設定會被忽略。";
                if (o.stuff != null && o.thing.MadeFromStuff && !ReplaceBuildingUtility.StuffAllowedFor(o.stuff, o.thing))
                    yield return $"{tag}: replacements[{i}] 的 stuff {o.stuff.defName} 不符合 {o.thing.defName} 的 stuffCategories，會退回預設材質。";
                if (requireSameSize && owner != null && o.thing.size != owner.size)
                    yield return $"{tag}: replacements[{i}] ({o.thing.defName}) 尺寸 {o.thing.size} 與原建築 {owner.size} 不同，在 requireSameSize=true 下永遠不會被抽到。";
            }

            if (owner != null && ValidOptions.Count > 0 && requireSameSize &&
                !ValidOptions.Any(o => o.thing.size == owner.size))
                yield return $"{tag}: requireSameSize=true 但沒有任何同尺寸候選，此擴充實際上不會作用。";
        }
    }
}
