using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Fortified.Structures
{
    /// <summary>增補物件的擺放規則。Where an augment entry places its things.</summary>
    public enum AugmentPlacement
    {
        /// <summary>結構外緣一圈（足跡外 1~2 格），朝外。The ring just outside the footprint, facing out.</summary>
        Perimeter,
        /// <summary>門的室外側、門洞兩旁（不擋門），朝外。Beside the outdoor side of a door (never in front of it), facing out.</summary>
        Entrance,
        /// <summary>結構內有屋頂的地板。Roofed floor inside the footprint.</summary>
        Indoor,
        /// <summary>貼著牆（足跡內外皆可），背對牆。Against a wall, inside or outside, facing away from it.</summary>
        WallMount,
        /// <summary>結構外圍的空地（距足跡 3~outskirtsRange 格），朝外。Open ground around the compound, facing out.</summary>
        Outskirts,
    }

    /// <summary>
    /// 增補清單裡的一項：放什麼、放幾個、怎麼放、要不要接電。
    /// One augment entry: what to place, how many, where, and whether to wire it up.
    /// </summary>
    public class StructureAugment
    {
        public ThingDef thing;
        public ThingDef stuff;
        public AugmentPlacement placement = AugmentPlacement.Perimeter;

        /// <summary>朝向：Outward 背向結構中心（或背向牆、門），None 隨機。Facing: Outward away from the centre (or wall/door); None random.</summary>
        public FFF_FacingMode facing = FFF_FacingMode.Outward;

        public IntRange count = new IntRange(1, 1);

        /// <summary>選填：站點威脅點數 → 數量，夾在 count 內。Optional: site threat points → count, clamped to count.</summary>
        public SimpleCurve countByPoints;

        /// <summary>每一級戒備額外增加的數量（小數隨機進位）。Extra things per readiness level (the fraction rounds randomly).</summary>
        public float countPerLevel;

        public float chance = 1f;

        /// <summary>戒備等級達到此值才放。Lowest readiness level that places this entry.</summary>
        public int minLevel;

        /// <summary>與同類及其他增補物件至少相隔幾格。Minimum gap to other augment things.</summary>
        public int minSpacing = 6;

        /// <summary>
        /// WallMount 只用外牆（面前是室外）。牆上攝影機用它，才會盯著來路而不是走廊。牆面附掛物（isAttachment）一律走 WallMount。
        /// WallMount on outer walls only (open sky in front). Wall cameras use it to watch the approaches, not corridors.
        /// Wall attachments (isAttachment) always use WallMount.
        /// </summary>
        public bool exteriorOnly;

        /// <summary>Outskirts 的最遠距離（格）。Outer distance of the Outskirts band.</summary>
        public int outskirtsRange = 14;

        /// <summary>
        /// 需要電力時，沿最短路徑鋪電線接到附近（powerSearchRadius 內）同派系的電網；找不到時改放 powerSource（例如電池）。
        /// For powered things, lay conduit along the shortest path to a same-faction power net within powerSearchRadius;
        /// when there is none, place powerSource (e.g. a battery) next to it instead.
        /// </summary>
        public bool connectPower = true;
        public int powerSearchRadius = 30;
        public ThingDef powerSource;
    }

    /// <summary>
    /// 結構增補：不改佈局檔，就把新內容（預警設施、砲塔、補給箱……）依規則加進既有結構。
    /// 符合 targetTags（結構標籤或 GenStep 的 useTag）或 targetStructures 的結構，在 FFF 結構 GenStep 生成主結構後套用。
    ///
    /// Structure augments: add new content (early-warning gear, turrets, supply crates…) to existing layouts by rule,
    /// without editing the layout files. Applies after an FFF structure GenStep spawns its main structure, to any
    /// structure matching targetTags (its tags or the GenStep's useTag) or targetStructures.
    /// <code>
    /// &lt;Fortified.Structures.StructureAugmentDef&gt;
    ///   &lt;defName&gt;MyMod_Watchtowers&lt;/defName&gt;
    ///   &lt;targetTags&gt;&lt;li&gt;MyMod_Outpost&lt;/li&gt;&lt;/targetTags&gt;
    ///   &lt;entries&gt;
    ///     &lt;li&gt;&lt;thing&gt;MyMod_Camera&lt;/thing&gt;&lt;placement&gt;Perimeter&lt;/placement&gt;&lt;count&gt;2~3&lt;/count&gt;&lt;/li&gt;
    ///   &lt;/entries&gt;
    /// &lt;/Fortified.Structures.StructureAugmentDef&gt;
    /// </code>
    /// </summary>
    public class StructureAugmentDef : Def
    {
        public List<string> targetTags;
        public List<string> targetStructures;

        /// <summary>選填：只套用在這些派系的站點。Optional: only sites of these factions.</summary>
        public List<FactionDef> factions;

        /// <summary>minLevel / countPerLevel 讀哪個戒備；未填時用站點派系對應的戒備。Readiness read by minLevel / countPerLevel; defaults to the site faction's.</summary>
        public ReadinessDef readiness;

        public List<StructureAugment> entries = new List<StructureAugment>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (targetTags.NullOrEmpty() && targetStructures.NullOrEmpty()) yield return "no targetTags or targetStructures; the augment never applies";
            if (entries.NullOrEmpty()) yield return "entries is empty";
            for (int i = 0; i < (entries?.Count ?? 0); i++)
            {
                StructureAugment entry = entries[i];
                if (entry?.thing == null) yield return $"entries[{i}] has no thing";
                else if (entry.thing.MadeFromStuff && entry.stuff == null) yield return $"entries[{i}] ({entry.thing.defName}) is made from stuff but has no stuff";
            }
        }
    }
}
