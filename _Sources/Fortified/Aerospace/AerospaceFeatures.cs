namespace Fortified
{
    /// <summary>
    /// 轉移船、結構樁、飛彈、防空等功能開關名稱（<see cref="FFF_FeatureDef"/>：轉移船、結構樁在 1.7/Odyssey/Defs/Aerospace/FFF_AerospaceFeatures.xml，只在 Odyssey 啟用時載入；
    /// 飛彈、海上平台在 1.7/Missiles/Defs/Aerospace/FFF_MissileFeatures.xml，一律載入）。
    /// 沒有模組用到時，轉移船的補丁不掛、元件不建立（docs: FeatureSwitch_Usage.md）。
    /// </summary>
    public static class FFF_AerospaceFeatures
    {
        public const string Transfer = "FFF_Feature_Transfer";
        public const string StructuralPiles = "FFF_Feature_StructuralPiles";
        /// <summary>
        /// 飛彈本體、裝配、打擊、BVR 防空、雷達、電子戰、砲兵、導彈陣地與封鎖空域。這些部分目前互相取用元件
        /// （例：飛彈發射時登記防空波次），先合成一項功能；介面化之後再拆（docs: 轉移船與BVR移植FFF_可行性評估.md §6.4）。
        /// </summary>
        public const string Missiles = "FFF_Feature_Missiles";
        public const string SeaPlatform = "FFF_Feature_SeaPlatform";

        /// <summary>Harmony 補丁的分類（<see cref="FFF_FeatureDef.harmonyCategory"/>）。</summary>
        public const string TransferPatches = "FFF.Transfer";
        public const string MissilePatches = "FFF.Missiles";
        public const string SeaPlatformPatches = "FFF.SeaPlatform";
    }
}
