using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace Fortified
{
    public class CompProperties_UseEffect_GiveTimedSiteQuest : CompProperties_UseEffect
    {
        /// <summary>要發放的任務腳本。The quest script to hand out.</summary>
        public QuestScriptDef quest;

        /// <summary>逾期基準天數（threatScale = 1 時）。Base expiry days at threatScale 1.</summary>
        public float baseTimeoutDays = 16f;
        public float minTimeoutDays = 4f;
        public float maxTimeoutDays = 30f;

        /// <summary>同一時間只允許一個進行中的任務。Only one active instance at a time.</summary>
        public bool onlyOneActive = true;

        /// <summary>訊息翻譯鍵，可由 XML 覆寫。Message translation keys, overridable from XML.</summary>
        [NoTranslate] public string alreadyActiveKey = "FFF_TimedSite_AlreadyActive";
        [NoTranslate] public string cannotFindSiteKey = "FFF_TimedSite_CannotFindSite";

        public CompProperties_UseEffect_GiveTimedSiteQuest()
        {
            compClass = typeof(CompUseEffect_GiveTimedSiteQuest);
        }
    }

    /// <summary>
    /// 使用後發放一個帶時限的站點任務；時限依難度縮放（難度越高時間越短）。
    /// UseEffect that grants a time-limited site quest on activation; the time
    /// limit scales with difficulty (harder = shorter).
    /// </summary>
    public class CompUseEffect_GiveTimedSiteQuest : CompUseEffect
    {
        public CompProperties_UseEffect_GiveTimedSiteQuest Props => (CompProperties_UseEffect_GiveTimedSiteQuest)props;

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);
            if (Props.quest == null) return;

            Map map = usedBy.MapHeld ?? parent.MapHeld ?? Find.AnyPlayerHomeMap;

            Slate slate = new Slate();
            slate.Set("points", StorytellerUtility.DefaultSiteThreatPointsNow());
            slate.Set("timeoutTicks", FFF_TimedSiteUtility.QuestTimeoutTicks(Props.baseTimeoutDays, Props.minTimeoutDays, Props.maxTimeoutDays));
            slate.Set("asker", usedBy);
            if (map != null) slate.Set("map", map);

            if (!Props.quest.CanRun(slate, Find.World))
            {
                Messages.Message(Props.cannotFindSiteKey.Translate(), parent, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(Props.quest, slate);
            if (!quest.hidden && quest.root.sendAvailableLetter)
            {
                QuestUtility.SendLetterQuestAvailable(quest);
            }
        }

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            if (Props.onlyOneActive && Props.quest != null)
            {
                var quests = Find.QuestManager.QuestsListForReading;
                for (int i = 0; i < quests.Count; i++)
                {
                    if (quests[i].root == Props.quest && !quests[i].Historical)
                    {
                        return new AcceptanceReport(Props.alreadyActiveKey.Translate());
                    }
                }
            }
            return base.CanBeUsedBy(p);
        }
    }
}
