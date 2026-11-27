using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 飛彈、防空與打擊的玩家設定（2026-11 從 DMSE 的 PlayerConfigSettings 移入；設定檔名稱不同，舊值不搬遷）。
    /// </summary>
    public class FFF_AerospaceSettings : ModSettings
    {
        public bool showBVRWorldCoverage;
        public bool strikeWaveReadyAlert = true;
        public bool godModeInstantAssembly = true;
        /// <summary>攔截殘骸落地造成小額鈍傷（預設關閉：只有視覺）。</summary>
        public bool interceptDebrisDamage;
        /// <summary>攔截特效密度：0 = 低、1 = 中（預設）、2 = 高。</summary>
        public int interceptFxDensity = 1;
        /// <summary>迫近防禦砲塔（CIWS）整體攔截率倍率：1 = 原值，0 = 永不擊落，2 = 加倍（仍以 100% 為上限）。</summary>
        public float ciwsInterceptMultiplier = 1f;
        /// <summary>末端落點標記：0 關閉、1 只顯示敵方、2 敵我雙方（預設）。</summary>
        public int terminalImpactMarkerMode = 2;
        /// <summary>NPC 可發射分導彈頭與誘餌（只有準則允許的陣營）。預設關。</summary>
        public bool npcMirvEnabled;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref showBVRWorldCoverage, "showBVRWorldCoverage");
            Scribe_Values.Look(ref strikeWaveReadyAlert, "strikeWaveReadyAlert", true);
            Scribe_Values.Look(ref godModeInstantAssembly, "godModeInstantAssembly", true);
            Scribe_Values.Look(ref interceptDebrisDamage, "interceptDebrisDamage", false);
            Scribe_Values.Look(ref interceptFxDensity, "interceptFxDensity", 1);
            Scribe_Values.Look(ref ciwsInterceptMultiplier, "ciwsInterceptMultiplier", 1f);
            Scribe_Values.Look(ref terminalImpactMarkerMode, "terminalImpactMarkerMode", 2);
            Scribe_Values.Look(ref npcMirvEnabled, "npcMirvEnabled", false);
        }
    }

    /// <summary>
    /// FFF 飛彈與防空的設定頁。只有用到飛彈或防空功能時才出現在模組設定清單（<see cref="SettingsCategory"/> 回傳空字串時不列出）。
    /// </summary>
    public class FFF_AerospaceMod : Mod
    {
        public static FFF_AerospaceSettings Settings { get; private set; }

        public FFF_AerospaceMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<FFF_AerospaceSettings>();
        }

        private static bool AnyFeature => FFF_FeatureRegistry.IsEnabled(FFF_AerospaceFeatures.Missiles);

        public override string SettingsCategory() => AnyFeature ? "FFF.Aerospace.Settings.Category".Translate().ToString() : string.Empty;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            bool missiles = FFF_FeatureRegistry.IsEnabled(FFF_AerospaceFeatures.Missiles);
            bool airDefense = missiles;
            if (MultiplayerCompat.MultiplayerModActive)
            {
                // 航太功能沒有做 Multiplayer 同步（MultiplayerCompat）：設定頁最上方提醒。
                Color color = GUI.color;
                GUI.color = ColorLibrary.RedReadable;
                listing.Label("FFF.Aerospace.Settings.MultiplayerWarning".Translate());
                GUI.color = color;
                listing.GapLine();
            }
            if (airDefense)
            {
                listing.CheckboxLabeled("FFF.BVR.World.ShowCoverage".Translate(), ref Settings.showBVRWorldCoverage,
                    "FFF.BVR.World.ShowCoverageDesc".Translate());
            }
            if (missiles)
            {
                listing.CheckboxLabeled("FFF.Strike.Settings.WaveReadyAlert".Translate(), ref Settings.strikeWaveReadyAlert,
                    "FFF.Strike.Settings.WaveReadyAlertDesc".Translate());
                listing.CheckboxLabeled("FFF.Missile.Settings.GodModeInstantAssembly".Translate(), ref Settings.godModeInstantAssembly,
                    "FFF.Missile.Settings.GodModeInstantAssemblyDesc".Translate());
            }
            if (airDefense)
            {
                listing.CheckboxLabeled("FFF.BVR.Settings.InterceptDebrisDamage".Translate(), ref Settings.interceptDebrisDamage,
                    "FFF.BVR.Settings.InterceptDebrisDamageDesc".Translate());
                listing.Label("FFF.BVR.Settings.InterceptFxDensity".Translate(), tooltip: "FFF.BVR.Settings.InterceptFxDensityDesc".Translate());
                for (int level = 0; level <= 2; level++)
                {
                    if (listing.RadioButton(("FFF.BVR.Settings.InterceptFxDensity" + level).Translate(), Settings.interceptFxDensity == level, 12f))
                    {
                        Settings.interceptFxDensity = level;
                    }
                }
                listing.Label("FFF.BVR.Settings.CiwsInterceptMultiplier".Translate(Settings.ciwsInterceptMultiplier.ToStringPercent()),
                    tooltip: "FFF.BVR.Settings.CiwsInterceptMultiplierDesc".Translate());
                Settings.ciwsInterceptMultiplier = Mathf.Round(listing.Slider(Settings.ciwsInterceptMultiplier, 0f, 2f) * 20f) / 20f;
                listing.Label("FFF.BVR.Settings.TerminalImpactMarkers".Translate(), tooltip: "FFF.BVR.Settings.TerminalImpactMarkersDesc".Translate());
                for (int mode = 0; mode <= 2; mode++)
                {
                    if (listing.RadioButton(("FFF.BVR.Settings.TerminalImpactMarkers" + mode).Translate(), Settings.terminalImpactMarkerMode == mode, 12f))
                    {
                        Settings.terminalImpactMarkerMode = mode;
                    }
                }
            }
            if (missiles && airDefense)
            {
                listing.CheckboxLabeled("FFF.BVR.Settings.NpcMirv".Translate(), ref Settings.npcMirvEnabled, "FFF.BVR.Settings.NpcMirvDesc".Translate());
            }
            listing.End();
        }
    }
}
