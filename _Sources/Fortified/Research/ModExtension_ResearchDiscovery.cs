using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 研究「探明」機制的加入開關（opt-in）。可掛在兩種 Def 上：
    ///
    /// 1. <see cref="ResearchTabDef"/>：該分頁底下的所有專案一併套用探明機制。
    /// 2. <see cref="ResearchProjectDef"/>：單一專案覆寫分頁設定，
    ///    例如把 <see cref="hideUntilDiscovered"/> 設為 false，讓入口專案永遠可見。
    ///
    /// 沒有掛上本擴充的分頁／專案完全不受影響。這是本機制唯一的啟用途徑，
    /// 因此任何未預期的解析失敗都只會退回「不隱藏」，不會讓別人的研究樹消失。
    /// </summary>
    public class ModExtension_ResearchDiscovery : DefModExtension
    {
        /// <summary>
        /// 前置研究尚未全部完成時，是否把此專案視為「未探明」而隱藏。
        /// false ＝本專案（或本分頁）不套用探明機制。
        /// </summary>
        public bool hideUntilDiscovered = true;

        /// <summary>新專案被探明時是否發信通知玩家。</summary>
        public bool discoveryLetter = true;

        /// <summary>可選：自訂探明信件的 LetterDef；留空則使用 PositiveEvent。</summary>
        public LetterDef letterDef;
    }
}
