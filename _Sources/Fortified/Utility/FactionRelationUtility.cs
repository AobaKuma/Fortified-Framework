using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 把派系關係「設定為」確切狀態，而不是「調整」。用於劇情上的確定結果：休戰、宣判、制裁、開局指定好感度。
    ///
    /// 原版 <see cref="Faction.TryAffectGoodwillWith"/> 不適合用來重置關係：
    /// 1. CalculateAdjustedGoodwillChange 會往自然好感度多推最多 25%，從 -100 重置到 0 實際會落在 +25 左右；
    /// 2. 玩家正在攻擊對方據點、或有任務鎖住好感度時，CanChangeGoodwillFor 直接拒絕，好感度原封不動；
    /// 3. 對有好感度的派系呼叫 SetRelationDirect 只會記錯誤、不會生效，關係類型只跟著門檻走。
    ///
    /// 這裡繞過上面三項「軟性」限制，但仍遵守「硬性」限制：沒有好感度、永久敵對、已被消滅的派系不會被改動。
    ///
    /// Sets a faction relation to an exact state instead of nudging it, for definite story outcomes
    /// (truces, verdicts, sanctions, scenario-forced goodwill). Vanilla TryAffectGoodwillWith adds up to 25% toward
    /// natural goodwill, is refused while the player attacks one of their settlements or a quest locks goodwill, and
    /// SetRelationDirect only logs an error for goodwill factions. This bypasses those soft limits but still respects
    /// the hard ones: factions without goodwill, permanent enemies and defeated factions are left alone.
    /// </summary>
    public static class FactionRelationUtility
    {
        /// <summary>
        /// 這對派系的關係能否被強制設定（硬性限制）。Whether the relation may be forced at all (hard limits only).
        /// </summary>
        public static bool CanForceRelation(Faction faction, Faction other)
        {
            if (faction == null || other == null || faction == other) return false;
            if (!faction.HasGoodwill || !other.HasGoodwill) return false;
            if (faction.defeated || other.defeated) return false;
            if (faction.def.permanentEnemy || other.def.permanentEnemy) return false;
            if (faction.def.permanentEnemyToEveryoneExceptPlayer && !other.IsPlayer) return false;
            if (other.def.permanentEnemyToEveryoneExceptPlayer && !faction.IsPlayer) return false;
            if (faction.def.permanentEnemyToEveryoneExcept != null && !faction.def.permanentEnemyToEveryoneExcept.Contains(other.def)) return false;
            if (other.def.permanentEnemyToEveryoneExcept != null && !other.def.permanentEnemyToEveryoneExcept.Contains(faction.def)) return false;
            return true;
        }

        /// <summary>
        /// 設定確切好感度與關係類型（雙向）。關係改變時照原版通知（囚犯身分、Lord、據點、信件）。
        /// 類型會修正成與好感度門檻一致，否則下一次 CheckKindThresholds 會把它翻回去。
        /// other 為 null 時對象是玩家。回傳是否有套用。
        ///
        /// Sets exact goodwill and relation kind on both sides and runs vanilla's relation-change notifications
        /// (prisoner status, lords, sites, letters). The kind is corrected to agree with the goodwill thresholds, or
        /// the next CheckKindThresholds would flip it. other defaults to the player. Returns whether it applied.
        /// </summary>
        public static bool SetGoodwillAndKind(Faction faction, int goodwill, FactionRelationKind kind,
            Faction other = null, bool canSendLetter = false, HistoryEventDef reason = null)
        {
            other ??= Faction.OfPlayer;
            if (!CanForceRelation(faction, other)) return false;

            goodwill = Mathf.Clamp(goodwill, -100, 100);
            kind = KindAgreeingWith(goodwill, kind);

            FactionRelation mine = faction.RelationWith(other);
            FactionRelation theirs = other.RelationWith(faction);
            int change = goodwill - mine.baseGoodwill;
            if (reason != null && change != 0 && (faction.IsPlayer || other.IsPlayer))
            {
                Faction affected = faction.IsPlayer ? other : faction;
                Find.HistoryEventsManager.RecordEvent(new HistoryEvent(reason,
                    affected.Named(HistoryEventArgsNames.AffectedFaction), change.Named(HistoryEventArgsNames.CustomGoodwill)));
            }

            FactionRelationKind previous = mine.kind;
            mine.baseGoodwill = goodwill;
            theirs.baseGoodwill = goodwill;
            mine.kind = kind;
            theirs.kind = kind;
            if (previous != kind)
            {
                faction.Notify_RelationKindChanged(other, previous, canSendLetter, null, GlobalTargetInfo.Invalid, out bool sentLetter);
                other.Notify_RelationKindChanged(faction, previous, canSendLetter && !sentLetter, null, GlobalTargetInfo.Invalid, out _);
            }
            return true;
        }

        /// <summary>
        /// 只設定確切好感度；關係類型依原版門檻從目前類型推得（例如敵對到 >= 0 才轉中立）。
        /// Sets exact goodwill only; the kind follows vanilla's thresholds from the current kind
        /// (e.g. hostile only turns neutral at >= 0).
        /// </summary>
        public static bool SetGoodwill(Faction faction, int goodwill, Faction other = null,
            bool canSendLetter = false, HistoryEventDef reason = null)
        {
            other ??= Faction.OfPlayer;
            if (faction == null) return false;
            return SetGoodwillAndKind(faction, goodwill, faction.RelationKindWith(other), other, canSendLetter, reason);
        }

        /// <summary>
        /// 修正成與好感度一致的關係類型。原版門檻（FactionRelation.CheckKindThresholds）：
        /// &lt;= -75 敵對、&gt;= 75 盟友；敵對要回到 &gt;= 0 才轉中立，盟友要掉到 &lt;= 0 才轉中立。
        /// The relation kind consistent with goodwill. Vanilla: &lt;= -75 hostile, &gt;= 75 ally; hostile turns
        /// neutral at &gt;= 0, ally at &lt;= 0.
        /// </summary>
        public static FactionRelationKind KindAgreeingWith(int goodwill, FactionRelationKind kind)
        {
            if (goodwill <= -75) return FactionRelationKind.Hostile;
            if (goodwill >= 75) return FactionRelationKind.Ally;
            if (kind == FactionRelationKind.Hostile && goodwill >= 0) return FactionRelationKind.Neutral;
            if (kind == FactionRelationKind.Ally && goodwill <= 0) return FactionRelationKind.Neutral;
            return kind;
        }
    }
}
