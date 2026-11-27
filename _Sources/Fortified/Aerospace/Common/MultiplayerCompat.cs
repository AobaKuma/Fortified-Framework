using Verse;

namespace Fortified
{
    /// <summary>
    /// Multiplayer（rwmt.Multiplayer）相容狀態。航太功能（飛彈、防空、電子戰、轉移艦）沒有做同步：
    /// 玩家操作（Gizmo、分頁、打擊規劃、裝配台、戰情終端）沒有註冊成同步動作，聯機時會不同步。
    /// 已處理的部分是模擬本身的確定性：裝置註冊表依 thingIDNumber 排序（<see cref="OrderedCompSet{T}"/>），
    /// 特效亂數用獨立狀態，會落地的主殘骸數量不受顯示設定影響。
    /// 2026-11 時 Multiplayer 只支援到 RimWorld 1.6，航太功能只有 1.7，所以目前兩者不會同時載入；這裡先在 Log 與設定頁提醒。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MultiplayerCompat
    {
        private const string PackageId = "rwmt.Multiplayer";

        /// <summary>Multiplayer 模組是否啟用（不管目前是不是聯機中）。</summary>
        public static readonly bool MultiplayerModActive = ModsConfig.IsActive(PackageId) || ModsConfig.IsActive(PackageId + "_steam");

        static MultiplayerCompat()
        {
            if (!MultiplayerModActive) { return; }
            // 功能開關在 HarmonyEntry 的靜態建構子決定；靜態建構子之間沒有固定順序，先確保它跑過。
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(HarmonyEntry).TypeHandle);
            if (!MultiplayerModActive || !FFF_FeatureRegistry.IsEnabled(FFF_AerospaceFeatures.Missiles)) { return; }
            Log.Warning("[Fortified] Multiplayer is active, but FFF's missile, air-defense and transfer-ship features are not synchronized for multiplayer. "
                + "Player actions on these systems (gizmos, war room, strike planning, missile assembly) will desync a multiplayer game. "
                + "Mod settings that change combat (CIWS intercept multiplier, intercept debris damage, NPC MIRV) must be identical for every player.");
        }
    }
}
