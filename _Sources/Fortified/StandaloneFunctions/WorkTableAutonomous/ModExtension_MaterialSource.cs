using Verse;

namespace Fortified
{
    /// <summary>
    /// 掛在「儲存建築」的 def 上，宣告它可以被連結的 <see cref="Building_WorkTableAutonomous"/> 直接抽取原料。
    /// <para>
    /// 這個標記是必要的，不能改成「只要是儲存型 facility 就自動抽」。工具櫃、機械櫃那類純加成建築
    /// 有時候也持有內容物（minified 物件、模組塞進去的雜物），自動判定會把別人的東西一起吃掉，
    /// 而且下游 mod 沒有任何辦法排除。要餵料就明講。
    /// </para>
    /// <para>
    /// 連結關係本身仍然由原版 facility 系統決定，這個 extension 不會建立連結：
    /// 工作台側的 <c>CompProperties_AffectedByFacilities.linkableFacilities</c> 要列出這個儲存建築的 def，
    /// 而儲存建築側的 <c>CompProperties_Facility</c> 預設 <c>maxSimultaneous=1</c>、<c>maxDistance=8</c>、
    /// <c>requiresLOS=true</c>，想多接幾座或拉遠距離都得自己放寬。
    /// </para>
    /// <para>
    /// 支援兩種儲存實作：格子型（<c>ISlotGroupParent</c>，例如貨架 <c>Building_Storage</c>，物品實際
    /// spawn 在地圖格上）與容器型（<c>IThingHolder</c>，例如書櫃那種把物品收在 <c>ThingOwner</c> 裡的建築）。
    /// 兩者兼具的建築只會被列舉一次。
    /// </para>
    /// </summary>
    public class ModExtension_MaterialSource : DefModExtension
    {
        /// <summary>
        /// 未通電／被關閉時（<c>CompAffectedByFacilities.IsFacilityActive</c> 為 false）是否仍可抽料。
        /// 預設 false：斷電的倉庫就該停止供料，這樣玩家看得懂為什麼產線停了。
        /// </summary>
        public bool allowWhileInactive = false;

        /// <summary>
        /// 是否跳過已經被殖民者預約的物件。預設 true——搶走小人正要搬的東西不會壞存檔，
        /// 但會讓對方的工作在半路失敗，在玩家眼裡就是 bug。
        /// </summary>
        public bool respectReservations = true;
    }
}
