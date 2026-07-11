using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 限時站點的難度縮放工具。難度越高（threatScale 越大），玩家能得到的時間越少。
    /// Difficulty scaling helpers for timed hostile sites. Higher difficulty
    /// (larger threatScale) means less time for the player.
    /// </summary>
    public static class FFF_TimedSiteUtility
    {
        private static float ThreatScale => Mathf.Max(Find.Storyteller?.difficulty?.threatScale ?? 1f, 0.2f);

        /// <summary>任務逾期時間（前往站點的期限）。Quest expiry window (time limit to reach the site).</summary>
        public static int QuestTimeoutTicks(float baseDays, float minDays, float maxDays)
        {
            float days = Mathf.Clamp(baseDays / ThreatScale, minDays, maxDays);
            return Mathf.RoundToInt(days * GenDate.TicksPerDay);
        }

        /// <summary>
        /// 進入站點後、敵方增援抵達前的倒數。
        /// Countdown after entering the site map before hostile reinforcements arrive.
        /// </summary>
        public static int EntryCountdownTicks(float baseHours, float minHours, float maxHours)
        {
            float hours = Mathf.Clamp(baseHours / ThreatScale, minHours, maxHours);
            return Mathf.RoundToInt(hours * GenDate.TicksPerHour);
        }
    }

    /// <summary>
    /// 掛在 SitePartDef 上的設定：進入站點後的增援倒數與通知信件。
    /// Mod extension for SitePartDefs: tunes the post-entry reinforcement
    /// countdown and its notification letter.
    /// </summary>
    public class FFF_TimedSiteExtension : DefModExtension
    {
        /// <summary>倒數基準小時（threatScale = 1 時）。Base countdown hours at threatScale 1.</summary>
        public float baseEntryHours = 36f;
        public float minEntryHours = 8f;
        public float maxEntryHours = 72f;

        /// <summary>信件翻譯鍵；文字鍵需含 {0}（剩餘時間）。Letter translation keys; the text key takes {0} = time left.</summary>
        [NoTranslate] public string entryLetterLabelKey = "FFF_TimedSite_EntryCountdownLabel";
        [NoTranslate] public string entryLetterTextKey = "FFF_TimedSite_EntryCountdownText";
    }
}
