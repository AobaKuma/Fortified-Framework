using Fortified;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using LudeonTK;

namespace AerospaceSample
{
    /// <summary>
    /// 修正鏈範例：地圖上的天氣降低防空偵測（地圖層預警窗口）與命中率。
    /// 在 AirDefenseExtensionDef 的 modifiers 列出就會生效，不必寫啟動程式碼。
    /// 修正不可有副作用：介面顯示也會呼叫。
    ///
    /// Modifier example: bad weather shortens map-layer warning windows and lowers hit chance.
    /// </summary>
    public class AirDefenseModifier_Weather : AirDefenseModifier
    {
        /// <summary>天氣影響的程度：0 = 不影響，1 = 直接乘上天氣的命中係數。</summary>
        public float strength = 1f;

        public override void Modify(AirDefenseQuery query, ref float value)
        {
            if (query.Layer != AirCombatLayer.Map || query.Map == null) { return; }
            if (query.Stat != AirDefenseStat.Detection && query.Stat != AirDefenseStat.HitChance) { return; }
            float weather = query.Map.weatherManager.CurWeatherAccuracyMultiplier;
            value *= Mathf.Lerp(1f, weather, strength);
        }
    }

    /// <summary>
    /// 事件處理者範例：開發者模式下把攔截結果與抵達寫進 Log。
    /// 處理者丟例外時 FFF 只會跳過它並記錄一次錯誤，不會中斷戰鬥流程。
    /// </summary>
    public class AirCombatListener_DevLog : AirCombatListener
    {
        public override void Notify(AirCombatEvent e)
        {
            if (!Prefs.DevMode) { return; }
            switch (e.Kind)
            {
                case AirCombatEventKind.InterceptResolved:
                    Log.Message($"[AerospaceSample] {e.Layer} intercept by {e.Defender?.Name ?? "?"} on {e.Track?.Self?.Label ?? e.TargetThing?.Label ?? "?"}: {e.Outcome} ({e.HitChance:P0})");
                    break;
                case AirCombatEventKind.TrackArrived:
                    Log.Message($"[AerospaceSample] {e.Track?.Self?.Label ?? e.TargetThing?.Label ?? "?"} arrived at {e.Site?.Label ?? "?"}");
                    break;
            }
        }
    }

    /// <summary>
    /// 否決者範例：節省彈藥，預計命中率太低就不開火。在動作之前被詢問（<see cref="AirCombatEvent.Pending"/> 為 true），
    /// 回傳 false 就不做：不扣彈藥、不進冷卻，鎖定會放掉，之後再問。不可有副作用，同一發可能每次掃描都被問。
    ///
    /// Veto example: hold fire when the expected hit chance is too low. Return false to block the action.
    /// </summary>
    public class AirCombatVeto_MinHitChance : AirCombatVeto
    {
        public float minHitChance = 0.15f;

        /// <summary>只管玩家的防空（NPC 照常開火）。</summary>
        public bool playerOnly = true;

        public override bool Allows(AirCombatEvent e)
        {
            if (e.Kind != AirCombatEventKind.InterceptorLaunched) { return true; }
            if (playerOnly && e.Defender != Faction.OfPlayer) { return true; }
            return e.HitChance >= minHitChance;
        }

        public override System.Collections.Generic.IEnumerable<string> ConfigErrors(Def parent)
        {
            if (minHitChance < 0f || minHitChance > 1f) { yield return $"{parent.defName}: minHitChance must be between 0 and 1"; }
        }
    }

    public static class DebugActions_AerospaceSample
    {
        /// <summary>
        /// 從目前地圖 20 格外派一架敵對偵察機飛向目前地圖。
        /// 地圖上要有 IR 感測器（或雷達）才看得到它；有雷射點防禦時會開火。
        /// </summary>
        [DebugAction("FFF Aerospace Sample", "Send hostile recon aircraft to current map", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SendReconAircraft()
        {
            Map map = Find.CurrentMap;
            Faction enemy = Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
            WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("AerospaceSample_ReconAircraft");
            if (map == null || enemy == null || def == null) { Messages.Message("No map, enemy faction or aircraft def.", MessageTypeDefOf.RejectInput, false); return; }
            if (!TryFindOrigin(map.Tile, 20, out PlanetTile origin)) { Messages.Message("No origin tile found.", MessageTypeDefOf.RejectInput, false); return; }
            var aircraft = (WorldObject_ReconAircraft)WorldObjectMaker.MakeWorldObject(def);
            aircraft.SetFaction(enemy);
            aircraft.Launch(origin, map.Tile);
            // IAirTrack 加入世界時 FFF 會自動通知防空網路，不必另外呼叫。
            Find.WorldObjects.Add(aircraft);
        }

        private static bool TryFindOrigin(PlanetTile center, int distance, out PlanetTile origin)
        {
            origin = PlanetTile.Invalid;
            for (int i = 0; i < 200; i++)
            {
                PlanetTile candidate = new PlanetTile(Rand.Range(0, center.Layer.TilesCount), center.Layer);
                float d = Find.WorldGrid.ApproxDistanceInTiles(center, candidate);
                if (d >= distance * 0.8f && d <= distance * 1.2f) { origin = candidate; return true; }
            }
            return false;
        }
    }
}
