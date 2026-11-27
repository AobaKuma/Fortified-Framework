using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Fortified
{
    // 2026-11 由列舉改成 Def（docs/BVR_框架化缺口.md 第 3 節），外部 mod 可以用 XML 新增值。
    // 出貨的 defName 與原本的列舉名稱相同：XML 的 <category>Guidance</category>、<sizeClass>Ballistic</sizeClass>、
    // <domains><li>Underwater</li></domains> 不必改；存檔裡以名稱保存的列舉值也照樣讀得回來（Scribe_Defs 以 defName 查找）。

    /// <summary>
    /// 導彈部件的槽位類別（原 <c>MissilePartCategory</c> 列舉）：彈體的 <c>slots</c> 列出有哪些槽，部件的 <c>category</c> 說明它放哪一槽。
    /// 新類別只要在 XML 加一個 Def：裝配台會多一個槽，部件的數值修正照常加總。
    /// 出貨的四個類別有專屬行為（戰鬥部效果、導引、射程、特殊酬載），由 <see cref="MissilePartCategoryDefOf"/> 判斷。
    /// </summary>
    public class MissilePartCategoryDef : Def
    {
        /// <summary>介面與型號命名的排列順序，小的在前。</summary>
        public int order;

        /// <summary>型號命名規則裡的符號（RulePack 的 [warhead] 之類）；null = defName 轉小寫。</summary>
        public string designKeyword;

        public string DesignKeyword => designKeyword ?? defName.ToLowerInvariant();

        private static List<MissilePartCategoryDef> ordered;

        /// <summary>全部類別，依 <see cref="order"/> 排序。</summary>
        public static List<MissilePartCategoryDef> AllOrdered
            => ordered ??= DefDatabase<MissilePartCategoryDef>.AllDefsListForReading.OrderBy(d => d.order).ThenBy(d => d.index).ToList();
    }

    /// <summary>
    /// 導彈尺寸分級（原 <c>MissileSizeClass</c> 列舉）：決定能放哪些發射平台與儲藏架。
    /// 原本寫死在程式裡的分級差異改成欄位。
    /// </summary>
    public class MissileSizeClassDef : Def
    {
        public int order;

        /// <summary>可以裝盤旋部件（原本只有巡飛彈）。</summary>
        public bool allowsLoiter;

        /// <summary>打擊規劃時佔用的地面通道數（原本彈道彈 2、其餘 1）。</summary>
        public int groundChannelCost = 1;

        /// <summary>戰情終端的分類禁射用哪一類；null = 不受分類禁射影響。</summary>
        public HoldFireClassDef holdFireClass;

        private static List<MissileSizeClassDef> ordered;

        public static List<MissileSizeClassDef> AllOrdered
            => ordered ??= DefDatabase<MissileSizeClassDef>.AllDefsListForReading.OrderBy(d => d.order).ThenBy(d => d.index).ToList();
    }

    /// <summary>
    /// 戰情終端可以個別禁射的目標類別（原 <c>HoldFireClass</c> 旗標列舉）。
    /// 飛彈看彈體的 <see cref="MissileSizeClassDef.holdFireClass"/>，空投艙是 <see cref="HoldFireClassDefOf.DropPod"/>，
    /// 外部航跡看 <see cref="IAirTrack.HoldFireClass"/>。新增 Def 後戰情終端會多一列開關。
    /// </summary>
    public class HoldFireClassDef : Def
    {
        public int order;

        private static List<HoldFireClassDef> ordered;

        public static List<HoldFireClassDef> AllOrdered
            => ordered ??= DefDatabase<HoldFireClassDef>.AllDefsListForReading.OrderBy(d => d.order).ThenBy(d => d.index).ToList();
    }

    /// <summary>
    /// 交戰領域（原 <c>CombatDomain</c> 列舉）：空中（導彈、空投艙、攔截彈的預設）或水下（其他 mod 的魚雷、潛航突擊艇），
    /// 其他 mod 可以再加。航跡的領域來自彈體（<see cref="MissileBodyDef.domain"/>），沒有彈體時來自來襲物的
    /// <see cref="AirTargetExtension.domain"/>。能接戰的一方各自宣告打哪些領域：火控雷達與 CIWS 用 <c>domains</c> 清單（空 = 只有空中），
    /// 攔截彈看彈體的領域，其他世界層效果器實作 <see cref="IDomainAwareEffector"/>（沒實作的只打空中）。
    /// 欄位為 null 一律當成空中（<see cref="CombatDomainDefOf.Air"/>）。
    /// </summary>
    public class CombatDomainDef : Def
    {
    }

    [DefOf]
    public static class MissilePartCategoryDefOf
    {
        [MayRequireOdyssey] public static MissilePartCategoryDef Warhead;
        [MayRequireOdyssey] public static MissilePartCategoryDef Guidance;
        [MayRequireOdyssey] public static MissilePartCategoryDef Propulsion;
        [MayRequireOdyssey] public static MissilePartCategoryDef Payload;

        static MissilePartCategoryDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(MissilePartCategoryDefOf));
    }

    [DefOf]
    public static class MissileSizeClassDefOf
    {
        [MayRequireOdyssey] public static MissileSizeClassDef Loitering;
        [MayRequireOdyssey] public static MissileSizeClassDef Light;
        [MayRequireOdyssey] public static MissileSizeClassDef Medium;
        [MayRequireOdyssey] public static MissileSizeClassDef Ballistic;

        static MissileSizeClassDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(MissileSizeClassDefOf));
    }

    [DefOf]
    public static class HoldFireClassDefOf
    {
        [MayRequireOdyssey] public static HoldFireClassDef Loitering;
        [MayRequireOdyssey] public static HoldFireClassDef DropPod;
        [MayRequireOdyssey] public static HoldFireClassDef Ballistic;

        static HoldFireClassDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(HoldFireClassDefOf));
    }

    [DefOf]
    public static class CombatDomainDefOf
    {
        [MayRequireOdyssey] public static CombatDomainDef Air;
        [MayRequireOdyssey] public static CombatDomainDef Underwater;

        static CombatDomainDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(CombatDomainDefOf));
    }
}
