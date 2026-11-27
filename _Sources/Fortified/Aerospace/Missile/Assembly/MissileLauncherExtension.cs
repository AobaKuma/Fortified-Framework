using System.Collections.Generic;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 掛在「發射平台」ThingDef 上的擴充，宣告此建築處理哪一階導彈、採用哪種發射方式、彈容量。
    /// 供裝填驗證、ITab、WorkGiver 等共用判定使用。
    ///
    /// 已與儲存架擴充合併：繼承 <see cref="MissileRackExtension"/>，因此 capacity / sizeClass /
    /// launchMode 皆由基底提供（單一真實來源，亦同時驅動容器渲染）。本類僅再加上 isStorage，
    /// 並作為「裝配發射平台」的型別標記——<see cref="MissileTaxonomy.PlatformAccepts(ThingDef, MissileBodyDef)"/>
    /// 只認得本類（純彈架／發射管使用基底，故不會被當成裝配平台）。
    ///
    /// 注意：垂直發射的「不可上重力船 + 單一朝向」限制由 XML 達成（rotatable=false 並掛
    /// 原版 PlaceWorker_InvalidOverSubstructure），此擴充僅描述資料，不重複實作該限制。
    /// 參見抽象母 Def FFF_VerticalLauncherBase / FFF_TiltLauncherBase（1.7/Missiles/Defs/Missile/FFF_MissileBases.xml）。
    /// </summary>
    public class MissileLauncherExtension : MissileRackExtension
    {
        /// <summary>是否為儲存類建築（彈架／彈藥庫），而非發射平台。</summary>
        public bool isStorage = false;

        /// <summary>
        /// 每次發射前的瞄準時間（ticks），給手搖／人工操作的發射架用；0 = 下令即射。
        /// 對地：下令後導彈留在架上，砲頭轉向出發方位，瞄準時間到才升空（打擊時間軸計入）；操作員離開時瞄準暫停。
        /// 攔截：砲頭對準同一個目標並持續追蹤滿這段時間才發射，換目標重新計時。
        /// </summary>
        public int aimTicks = 0;

        /// <summary>建築的瞄準時間（沒有本擴充時為 0）。</summary>
        public static int AimTicksOf(Thing launcher) => launcher?.def?.GetModExtension<MissileLauncherExtension>()?.aimTicks ?? 0;

        public MissileLauncherExtension()
        {
            // 發射平台沿用原本的預設彈容 1（基底 MissileRackExtension 預設為 4）。
            capacity = 1;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            // 基底會驗證 capacity >= 1。
            foreach (string e in base.ConfigErrors())
            {
                yield return e;
            }

            if (launchMode == MissileLaunchMode.Both)
            {
                yield return "MissileLauncherExtension.launchMode 不應為 Both（平台必須是 Tilt 或 Vertical）。";
            }
        }
    }

    /// <summary>導彈分級／發射相容性的共用判定。</summary>
    public static class MissileTaxonomy
    {
        /// <summary>彈體的發射方式是否相容於某平台的發射方式。</summary>
        public static bool LaunchModeCompatible(MissileLaunchMode bodyMode, MissileLaunchMode platformMode)
        {
            if (bodyMode == MissileLaunchMode.Both)
            {
                return true;
            }
            return bodyMode == platformMode;
        }

        /// <summary>
        /// 此平台（含 MissileLauncherExtension）能否裝填／儲存此彈體：分級（含 extraSizeClasses）需相符且發射方式相容。
        /// 執行期實際能裝什麼由建築的 storage filter 決定；本判定供內容檢查與 UI 對照，兩者應一致。
        /// </summary>
        public static bool PlatformAccepts(MissileLauncherExtension ext, MissileBodyDef body)
        {
            if (ext == null || body == null)
            {
                return false;
            }
            if (!ext.AcceptsSizeClass(body.sizeClass))
            {
                return false;
            }
            return LaunchModeCompatible(body.launchMode, ext.launchMode);
        }

        /// <summary>便利方法：直接由建築 def 取得擴充並判定。</summary>
        public static bool PlatformAccepts(ThingDef platformDef, MissileBodyDef body)
        {
            return PlatformAccepts(platformDef?.GetModExtension<MissileLauncherExtension>(), body);
        }
    }
}
