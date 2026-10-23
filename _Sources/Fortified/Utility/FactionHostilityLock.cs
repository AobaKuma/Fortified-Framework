using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 對玩家的永久敵對鎖。鎖住後：好感度設成 -100、關係為敵對；之後任何「提高」好感度的變化都會被擋下
    /// （自然回升、送禮、任務、和談），<see cref="FactionRelationUtility"/> 也不能強制改回；每 2500 tick 再確認一次仍是敵對。
    /// 降低好感度照常。存檔保存，只能由 <see cref="Unlock"/> 解除（劇情特例，例如老兵被認出）。
    ///
    /// Permanent hostility toward the player. Once locked the faction is set to -100 goodwill and Hostile; every goodwill
    /// increase is refused afterwards (natural drift, gifts, quests, peace talks), <see cref="FactionRelationUtility"/>
    /// can't force it back, and every 2500 ticks hostility is re-asserted. Decreases still apply. Saved with the game;
    /// only <see cref="Unlock"/> lifts it (a story exception, e.g. a recognised veteran).
    /// <code>
    /// FactionHostilityLock.Lock(faction);          // 永久敵對 lock
    /// FactionHostilityLock.IsLocked(faction);      // 查詢 query
    /// FactionHostilityLock.Unlock(faction);        // 劇情解除 story exception
    /// </code>
    /// </summary>
    public static class FactionHostilityLock
    {
        private static bool applying;

        public static GameComponent_FactionHostilityLock Component => Current.Game?.GetComponent<GameComponent_FactionHostilityLock>();

        public static bool IsLocked(Faction faction) => faction != null && Component != null && Component.Contains(faction);

        /// <summary>這一對派系是否被鎖（其中一方是玩家、另一方被鎖）。Whether the pair is locked (one side the player, the other locked).</summary>
        public static bool IsLockedPair(Faction a, Faction b)
        {
            if (a == null || b == null) return false;
            if (a.IsPlayer) return IsLocked(b);
            if (b.IsPlayer) return IsLocked(a);
            return false;
        }

        /// <summary>鎖死期間由框架以外的路徑強制改關係是否被擋。Whether forcing the relation from outside the lock is refused.</summary>
        public static bool BlocksForcing(Faction a, Faction b) => !applying && IsLockedPair(a, b);

        /// <summary>
        /// 鎖住並立即變成敵對。已鎖時不重複發信。回傳是否為新鎖。
        /// Locks and turns hostile at once. No second letter when already locked. Returns whether this was a new lock.
        /// </summary>
        public static bool Lock(Faction faction, string letterLabel = null, string letterText = null)
        {
            GameComponent_FactionHostilityLock component = Component;
            if (faction == null || faction.IsPlayer || component == null || !faction.HasGoodwill) return false;
            bool added = component.Add(faction);
            Enforce(faction);
            if (added && !letterLabel.NullOrEmpty())
            {
                Find.LetterStack.ReceiveLetter(letterLabel, letterText ?? string.Empty, LetterDefOf.ThreatBig, (LookTargets)null, faction);
            }
            return added;
        }

        public static bool Unlock(Faction faction)
        {
            return faction != null && Component != null && Component.Remove(faction);
        }

        /// <summary>把被鎖的派系拉回 -100 敵對。Pulls a locked faction back to -100 Hostile.</summary>
        internal static void Enforce(Faction faction)
        {
            if (faction == null || faction.defeated || !faction.HasGoodwill) return;
            if (faction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile && faction.BaseGoodwillWith(Faction.OfPlayer) <= -75) return;
            applying = true;
            try
            {
                FactionRelationUtility.SetGoodwillAndKind(faction, -100, FactionRelationKind.Hostile, Faction.OfPlayer, canSendLetter: false);
            }
            finally
            {
                applying = false;
            }
        }
    }

    public class GameComponent_FactionHostilityLock : GameComponent
    {
        private const int EnforceInterval = 2500;

        private List<Faction> locked = new List<Faction>();

        public GameComponent_FactionHostilityLock(Game game) { }

        public List<Faction> LockedForReading => locked;

        internal bool Contains(Faction faction) => locked.Contains(faction);

        internal bool Add(Faction faction)
        {
            if (locked.Contains(faction)) return false;
            locked.Add(faction);
            return true;
        }

        internal bool Remove(Faction faction) => locked.Remove(faction);

        public override void GameComponentTick()
        {
            if (locked.Count == 0 || Find.TickManager.TicksGame % EnforceInterval != 0) return;
            for (int i = locked.Count - 1; i >= 0; i--)
            {
                if (locked[i] == null) { locked.RemoveAt(i); continue; }
                FactionHostilityLock.Enforce(locked[i]);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref locked, "locked", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                locked ??= new List<Faction>();
                locked.RemoveAll(f => f == null);
            }
        }
    }

    /// <summary>被鎖的派系與玩家之間，拒絕任何提高好感度的變化。Refuses any goodwill increase between a locked faction and the player.</summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.CanChangeGoodwillFor))]
    public static class Patch_Faction_CanChangeGoodwillFor_HostilityLock
    {
        public static void Postfix(Faction __instance, Faction other, int goodwillChange, ref bool __result)
        {
            if (__result && goodwillChange > 0 && FactionHostilityLock.IsLockedPair(__instance, other)) __result = false;
        }
    }
}
