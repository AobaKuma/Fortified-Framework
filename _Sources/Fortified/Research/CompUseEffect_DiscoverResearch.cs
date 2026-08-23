using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 封存科技解封物：使用後隨機解封 <see cref="CompProperties_UseEffect_DiscoverResearch.researchProjects"/>
    /// 之中的一個專案（永久探明）並灌入一筆研究進度；
    /// 清單裡已經沒有可推進的專案時，改為給使用者智識（知識）技能經驗。
    ///
    /// 防禦性設計重點：
    /// - <see cref="DoEffect"/> 整段包在 try/catch 內。物品在 <c>CompUsable</c> 的流程中會被消耗，
    ///   若這裡拋例外，玩家會失去物品卻什麼都沒拿到，因此寧可吞掉例外並記錄。
    /// - 每一條失敗路徑都有可觀察的回饋（信件或訊息），不會出現「用了沒反應」。
    /// - 抽選使用固定種子（物品 ID × 遊戲刻），在 Multiplayer 下各客戶端結果一致。
    /// </summary>
    public class CompUseEffect_DiscoverResearch : CompUseEffect
    {
        // 抽選用暫存，避免每次使用都配置新 List。UI 執行緒單執行緒使用，不需要鎖。
        private static readonly List<ResearchProjectDef> tmpCandidates = new List<ResearchProjectDef>();

        public CompProperties_UseEffect_DiscoverResearch Props
        {
            get { return (CompProperties_UseEffect_DiscoverResearch)props; }
        }

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);
            try
            {
                Resolve(usedBy);
            }
            catch (Exception ex)
            {
                Log.Error($"[FFF] CompUseEffect_DiscoverResearch on {parent?.def?.defName ?? "(null)"} failed: {ex}");
                // 物品已經被消耗，至少把保底的經驗給出去，別讓玩家血本無歸。
                TryGrantFallback(usedBy, silentOnFailure: true);
            }
            finally
            {
                tmpCandidates.Clear();
            }
        }

        // -------------------------------------------------------------------------
        // 主流程
        // -------------------------------------------------------------------------

        private void Resolve(Pawn usedBy)
        {
            CompProperties_UseEffect_DiscoverResearch p = Props;
            if (p == null)
            {
                Log.Error($"[FFF] CompUseEffect_DiscoverResearch on {parent?.def?.defName ?? "(null)"} has wrong props type.");
                return;
            }

            // 尚未進入遊戲時（理論上不會發生）什麼都做不了，直接離開。
            if (Current.Game == null || Find.ResearchManager == null)
            {
                TryGrantFallback(usedBy, silentOnFailure: false);
                return;
            }

            ResearchProjectDef chosen = PickTarget(p);

            // 沒有可推進的目標，或抽到的專案已經研究完畢 → 給經驗。
            if (chosen == null || chosen.IsFinished)
            {
                TryGrantFallback(usedBy, silentOnFailure: false);
                return;
            }

            // 先記下狀態：解封與推進之後 IsUndiscovered 會改變，訊息要用改變前的判斷。
            bool wasSealed = ResearchDiscoveryUtility.IsUndiscovered(chosen);

            if (wasSealed)
            {
                // suppressLetter：本 Comp 自己會通知玩家，不需要 GameComponent 的通用探明信。
                ResearchDiscoveryUtility.ForceDiscover(chosen, suppressLetter: true);
            }

            bool progressed = false;
            if (p.progressAmount > 0f)
            {
                progressed = KnowledgeUtility.AddProgressTo(chosen, p.progressAmount, usedBy);
            }

            Announce(usedBy, chosen, wasSealed, progressed ? p.progressAmount : 0f);
        }

        /// <summary>
        /// 依設定挑出這次要推進的專案；沒有合適目標時回傳 null。
        /// 優先順序：未探明的封存科技 &gt; 已探明但未完成的專案。
        /// </summary>
        private ResearchProjectDef PickTarget(CompProperties_UseEffect_DiscoverResearch p)
        {
            List<ResearchProjectDef> list = p.researchProjects;
            if (list == null || list.Count == 0)
            {
                return null;
            }

            // skipCompletedProjects == false：整份清單均勻抽選，抽到已完成的就當作「已經研究過」。
            if (!p.skipCompletedProjects)
            {
                CollectCandidates(list, requireSealed: false, requireUnfinished: false);
                return RandomFrom(tmpCandidates);
            }

            // 第一順位：還沒被探明的封存科技。
            CollectCandidates(list, requireSealed: true, requireUnfinished: true);
            if (tmpCandidates.Count > 0)
            {
                return RandomFrom(tmpCandidates);
            }

            // 第二順位：已探明但還沒研究完的專案，單純推進度。
            if (p.includeDiscoveredProjects)
            {
                CollectCandidates(list, requireSealed: false, requireUnfinished: true);
                if (tmpCandidates.Count > 0)
                {
                    return RandomFrom(tmpCandidates);
                }
            }

            return null;
        }

        /// <summary>把符合條件的專案收進 <see cref="tmpCandidates"/>（去除 null 與重複項）。</summary>
        private static void CollectCandidates(List<ResearchProjectDef> source, bool requireSealed, bool requireUnfinished)
        {
            tmpCandidates.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                ResearchProjectDef proj = source[i];
                // DefDatabase 解析失敗的項目會還原成 null。
                if (proj == null || tmpCandidates.Contains(proj))
                {
                    continue;
                }
                if (requireUnfinished && proj.IsFinished)
                {
                    continue;
                }
                if (requireSealed && !ResearchDiscoveryUtility.IsUndiscovered(proj))
                {
                    continue;
                }
                tmpCandidates.Add(proj);
            }
        }

        /// <summary>
        /// 從候選中抽一個。種子固定為（物品 ID, 目前遊戲刻），讓 Multiplayer 各客戶端得到同一結果；
        /// 同一刻使用兩個不同物品也不會撞號，因為 thingIDNumber 不同。
        /// </summary>
        private ResearchProjectDef RandomFrom(List<ResearchProjectDef> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            int thingId = parent != null ? parent.thingIDNumber : 0;
            int tick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;

            Rand.PushState(Gen.HashCombineInt(thingId, tick));
            try
            {
                return candidates[Rand.Range(0, candidates.Count)];
            }
            finally
            {
                Rand.PopState();
            }
        }

        // -------------------------------------------------------------------------
        // 保底：給技能經驗
        // -------------------------------------------------------------------------

        /// <summary>
        /// 給使用者技能經驗。<paramref name="silentOnFailure"/> 為 true 時（例外處理路徑）
        /// 不再跳訊息，避免同一次使用洗出兩三則提示。
        /// </summary>
        private void TryGrantFallback(Pawn usedBy, bool silentOnFailure)
        {
            CompProperties_UseEffect_DiscoverResearch p = Props;
            if (p == null)
            {
                return;
            }

            float xp = Mathf.Max(0f, p.fallbackXpAmount);
            SkillDef skill = null;
            try
            {
                skill = p.FallbackSkillResolved;
            }
            catch (Exception ex)
            {
                Log.Error($"[FFF] CompUseEffect_DiscoverResearch could not resolve fallback skill: {ex}");
            }

            bool granted = false;
            if (xp > 0f && skill != null && usedBy != null && usedBy.skills != null)
            {
                usedBy.skills.Learn(skill, xp, direct: true, ignoreLearnRate: p.fallbackXpIgnoreLearnRate);
                granted = true;
            }

            if (silentOnFailure)
            {
                return;
            }

            if (granted)
            {
                Message("FFF_Research_UnsealNothingMessage".Translate(
                        usedBy.Named("PAWN"),
                        Mathf.RoundToInt(xp).Named("XP"),
                        skill.LabelCap.Named("SKILL")),
                    MessageTypeDefOf.NeutralEvent, usedBy);
            }
            else
            {
                // 例如機械體使用、或設定把經驗關成 0：仍然要讓玩家知道發生了什麼。
                Message("FFF_Research_UnsealNothingNoGainMessage".Translate(),
                    MessageTypeDefOf.RejectInput, usedBy);
            }
        }

        // -------------------------------------------------------------------------
        // 通知
        // -------------------------------------------------------------------------

        private void Announce(Pawn usedBy, ResearchProjectDef proj, bool wasSealed, float progressGiven)
        {
            CompProperties_UseEffect_DiscoverResearch p = Props;
            int progressInt = Mathf.RoundToInt(progressGiven);

            if (!wasSealed)
            {
                // 只是推了進度，不值得一封信。
                Message("FFF_Research_UnsealProgressMessage".Translate(
                        proj.LabelCap.Named("PROJECT"),
                        progressInt.Named("PROGRESS")),
                    MessageTypeDefOf.PositiveEvent, usedBy);
                return;
            }

            TaggedString text = "FFF_Research_UnsealLetterText".Translate(
                proj.LabelCap.Named("PROJECT"),
                progressInt.Named("PROGRESS"));

            if (p != null && p.sendLetter && Find.LetterStack != null)
            {
                Find.LetterStack.ReceiveLetter(
                    "FFF_Research_UnsealLetterLabel".Translate(),
                    text,
                    p.letterDef ?? LetterDefOf.PositiveEvent,
                    usedBy != null && usedBy.Spawned ? (LookTargets)usedBy : LookTargets.Invalid);
                return;
            }

            Message(text, MessageTypeDefOf.PositiveEvent, usedBy);
        }

        private static void Message(TaggedString text, MessageTypeDef type, Pawn usedBy)
        {
            if (usedBy != null && usedBy.Spawned)
            {
                Messages.Message(text, usedBy, type, historical: false);
                return;
            }
            Messages.Message(text, type, historical: false);
        }
    }
}
