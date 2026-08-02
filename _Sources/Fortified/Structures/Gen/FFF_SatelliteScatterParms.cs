// 当白昼倾坠之时
namespace Fortified.Structures
{
    /// <summary>
    /// 衛星結構散射參數。用於 <see cref="GenStep_FFFStructureGen.satelliteScatter"/>：
    /// 在主結構外圍的環帶上，獨立生成數座帶有指定標籤的小型結構。
    ///
    /// Satellite scatter parameters. Each entry drops <see cref="count"/> small structures
    /// carrying <see cref="tag"/> on a ring around the main structure. Each piece is generated
    /// separately, so the main structure's sketch is never enlarged.
    ///
    /// <code>
    /// &lt;satelliteScatter&gt;
    ///   &lt;li&gt;
    ///     &lt;tag&gt;DMS_RuinSatellite&lt;/tag&gt;
    ///     &lt;count&gt;4&lt;/count&gt;
    ///     &lt;radiusPct&gt;0.16&lt;/radiusPct&gt;
    ///     &lt;minRadius&gt;20&lt;/minRadius&gt;
    ///     &lt;minSpacing&gt;4&lt;/minSpacing&gt;
    ///   &lt;/li&gt;
    /// &lt;/satelliteScatter&gt;
    /// </code>
    /// </summary>
    public class FFF_SatelliteScatterParms
    {
        /// <summary>要挑選的結構標籤。Tag the candidate structures must carry.</summary>
        public string tag;

        /// <summary>要生成幾座。How many to place.</summary>
        public int count = 1;

        /// <summary>
        /// 環半徑佔地圖邊長的比例。實際半徑取 max(minRadius, mapSize * radiusPct)。
        /// Ring radius as a fraction of map size; the larger of this and minRadius wins.
        /// </summary>
        public float radiusPct = 0.2f;

        /// <summary>環半徑下限（格）。小地圖上避免全部貼在主結構身上。
        /// Radius floor in cells, so small maps don't pile satellites onto the main structure.</summary>
        public int minRadius = 0;

        /// <summary>彼此以及與既有結構之間的最小間距（格）。Minimum gap from anything already placed.</summary>
        public int minSpacing = 4;

        /// <summary>是否隨機轉向。Randomise each piece's rotation.</summary>
        public bool randomRotation = false;

        /// <summary>
        /// 半徑抖動範圍，實際半徑會乘上這區間內的隨機值，讓環帶不要太像一個正圓。
        /// Radius jitter; keeps the ring from looking like a perfect circle.
        /// </summary>
        public float radiusJitterMin = 0.85f;
        public float radiusJitterMax = 1.25f;
    }
}
