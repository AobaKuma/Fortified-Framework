using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 「封存科技解封物」的設定。掛在任何具有 <see cref="CompUsable"/> 的 ThingDef 上。
    ///
    /// 使用後從 <see cref="researchProjects"/> 中隨機挑一個尚未解封的專案，
    /// 將其永久探明（見 <see cref="ResearchDiscoveryUtility.ForceDiscover"/>）並推進
    /// <see cref="progressAmount"/> 點進度；若清單裡的專案已經全部研究完畢，
    /// 則改為給使用者 <see cref="fallbackXpAmount"/> 點智識（知識）技能經驗。
    ///
    /// 注意：<c>List&lt;ResearchProjectDef&gt;</c> 中無法解析的 defName 會被 DefDatabase
    /// 靜默移除，清單可能比 XML 寫的短。本 Comp 的所有路徑都以「清單可能為空」為前提設計，
    /// 最差情況只會退回給經驗，不會拋例外，也不會讓物品變成用了沒反應。
    /// </summary>
    public class CompProperties_UseEffect_DiscoverResearch : CompProperties_UseEffect
    {
        /// <summary>可被本物品解封的封存科技清單。必填。</summary>
        public List<ResearchProjectDef> researchProjects;

        /// <summary>解封後立刻灌入的研究進度點數。</summary>
        public float progressAmount = 100f;

        /// <summary>
        /// true（預設）＝只從「還能推進」的專案中抽選，清單全部研究完畢時才退回給經驗。
        /// false ＝從整份清單均勻抽選，抽到已完成的專案就直接退回給經驗（可做成賭博式的物品）。
        /// </summary>
        public bool skipCompletedProjects = true;

        /// <summary>
        /// 找不到未探明的專案時，是否允許改推進「已探明但未完成」的專案。
        /// false ＝沒有可解封的目標就直接給經驗。僅在 <see cref="skipCompletedProjects"/> 為 true 時有意義。
        /// </summary>
        public bool includeDiscoveredProjects = true;

        /// <summary>退而求其次時給予經驗的技能；留空則使用智識（Intellectual）。</summary>
        public SkillDef fallbackSkill;

        /// <summary>退而求其次時給予的經驗值。預設對齊原生科技印記的 2000 點。</summary>
        public float fallbackXpAmount = 2000f;

        /// <summary>給經驗時是否忽略學習速度上限（原生科技印記為 true）。</summary>
        public bool fallbackXpIgnoreLearnRate = true;

        /// <summary>解封成功時是否發信通知；false 則只跳一則訊息。</summary>
        public bool sendLetter = true;

        /// <summary>解封信件的 LetterDef；留空則使用 PositiveEvent。</summary>
        public LetterDef letterDef;

        public CompProperties_UseEffect_DiscoverResearch()
        {
            compClass = typeof(CompUseEffect_DiscoverResearch);
        }

        /// <summary>取得實際要給經驗的技能。DefOf 在 Def 載入完成後才綁定，故延後到執行期才取。</summary>
        public SkillDef FallbackSkillResolved
        {
            get { return fallbackSkill ?? SkillDefOf.Intellectual; }
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef))
            {
                yield return e;
            }

            string name = parentDef != null ? parentDef.defName : "(null)";

            if (researchProjects == null || researchProjects.Count == 0)
            {
                yield return $"{name}: CompProperties_UseEffect_DiscoverResearch 需要至少一個 researchProjects 項目" +
                             "（無法解析的 defName 會被靜默移除，請確認拼字與載入順序）。";
            }
            else
            {
                for (int i = 0; i < researchProjects.Count; i++)
                {
                    if (researchProjects[i] == null)
                    {
                        yield return $"{name}: CompProperties_UseEffect_DiscoverResearch 的 researchProjects 第 {i} 項為 null。";
                    }
                }
            }

            if (progressAmount < 0f)
            {
                yield return $"{name}: CompProperties_UseEffect_DiscoverResearch 的 progressAmount 不可為負數。";
            }

            if (fallbackXpAmount < 0f)
            {
                yield return $"{name}: CompProperties_UseEffect_DiscoverResearch 的 fallbackXpAmount 不可為負數。";
            }
        }
    }
}
