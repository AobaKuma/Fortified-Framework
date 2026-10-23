using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Fortified;

/// <summary>
/// 依戒備等級增加的一級巡邏兵力。Extra patrol units that join from a readiness level up.
/// </summary>
public class ReadinessPatrolTier
{
    public int minLevel;
    /// <summary>基礎數量，除以等級節奏（tempo）後無條件進位。Base count, divided by the level tempo and rounded up.</summary>
    public float count = 1f;
    public List<PawnGenOption> options;
}

/// <summary>
/// 站點的巡邏增援：玩家在站點地圖上時，定期有巡邏隊從邊緣進入。節奏與兵力隨戒備等級變化；
/// 第一支巡邏隊抵達時交給 ReadinessWorker.OnPatrolArrived（例如宣告敵對），之後每支累積戒備壓力。
///
/// Patrol reinforcements for a site: while the player is on the site map, patrols walk in from the edge. Tempo and
/// strength follow the readiness level. The first arrival goes to ReadinessWorker.OnPatrolArrived (e.g. turn
/// hostile); later ones build readiness pressure.
/// </summary>
public class WorldObjectCompProperties_ReadinessPatrol : WorldObjectCompProperties
{
    /// <summary>未填時用站點派系對應的戒備。Defaults to the readiness of the site's faction.</summary>
    public ReadinessDef readiness;

    /// <summary>未填時用站點派系。Defaults to the site's faction.</summary>
    public FactionDef faction;

    /// <summary>基本巡邏兵種。Basic patrol units.</summary>
    public List<PawnGenOption> possibleGeneratePawn;

    /// <summary>舊版中型／重型欄位，等同 minLevel 1 / 3 的 tier。Legacy medium/heavy lists, same as tiers at minLevel 1 / 3.</summary>
    public List<PawnGenOption> possibleGeneratePawn_Medium;
    public List<PawnGenOption> possibleGeneratePawn_Heavy;
    public int mediumMinLevel = 1;
    public float mediumCount = 3f;
    public int heavyMinLevel = 3;
    public float heavyCount = 1f;

    public List<ReadinessPatrolTier> tiers;

    public int averagePawnCount = 5;
    public int spawnCountSigma = 5;

    /// <summary>兩次巡邏的最短間隔（tick）。Minimum ticks between patrols.</summary>
    public int MBTTime = 30000;
    public int firstPatrolMaxTicks = 180000;

    /// <summary>幾支後續巡邏累積成一級戒備。How many follow-up patrols add up to one readiness level.</summary>
    public int patrolCountToRaiseLevel = 3;

    /// <summary>後續巡邏的信件；可填 Keyed 鍵，{0} = 派系。Follow-up patrol letter; may be a Keyed key, {0} = faction.</summary>
    [MustTranslate] public string letterLabel;
    [MustTranslate] public string letterText;

    public WorldObjectCompProperties_ReadinessPatrol()
    {
        compClass = typeof(WorldObjectComp_ReadinessPatrol);
    }

    public IEnumerable<ReadinessPatrolTier> AllTiers()
    {
        if (!possibleGeneratePawn_Medium.NullOrEmpty())
            yield return new ReadinessPatrolTier { minLevel = mediumMinLevel, count = mediumCount, options = possibleGeneratePawn_Medium };
        if (!possibleGeneratePawn_Heavy.NullOrEmpty())
            yield return new ReadinessPatrolTier { minLevel = heavyMinLevel, count = heavyCount, options = possibleGeneratePawn_Heavy };
        if (tiers == null) yield break;
        foreach (ReadinessPatrolTier tier in tiers) yield return tier;
    }

    public override IEnumerable<string> ConfigErrors(WorldObjectDef parentDef)
    {
        foreach (string error in base.ConfigErrors(parentDef)) yield return error;
        if (!typeof(MapParent).IsAssignableFrom(parentDef.worldObjectClass)) yield return $"{parentDef.defName} has a readiness patrol but is not a MapParent";
        if (possibleGeneratePawn.NullOrEmpty()) yield return $"{parentDef.defName} readiness patrol has no possibleGeneratePawn";
        if (patrolCountToRaiseLevel <= 0) yield return "patrolCountToRaiseLevel must be positive";
    }
}

public class WorldObjectComp_ReadinessPatrol : WorldObjectComp
{
    // IsHashIntervalTick 需要固定常數，只用來節流；實際排程是絕對 tick。
    // IsHashIntervalTick needs a constant, so it only throttles; the schedule itself is an absolute tick.
    private const int SampleInterval = 250;

    private bool greetTriggered;
    private int nextPatrolTick;
    private int timeToSpawn;
    private int latestPatrolTick = -1;

    public WorldObjectCompProperties_ReadinessPatrol Props => (WorldObjectCompProperties_ReadinessPatrol)props;

    public ReadinessDef Readiness => Props.readiness ?? ReadinessUtility.DefFor(PatrolFaction);

    /// <summary>
    /// 巡邏派系：Props.faction，否則站點派系（玩家除外），否則 Props.readiness 的派系。站點沒有派系或被玩家接手時巡邏照樣是原派系。
    /// Patrol faction: Props.faction, else the site's (never the player), else Props.readiness's. A site with no faction or
    /// taken over by the player still gets its original patrols.
    /// </summary>
    public Faction PatrolFaction
    {
        get
        {
            if (Props.faction != null) return Find.FactionManager.FirstFactionOfDef(Props.faction);
            if (parent.Faction != null && !parent.Faction.IsPlayer) return parent.Faction;
            return Props.readiness?.Worker.Faction;
        }
    }

    private int Level => Readiness != null ? ReadinessUtility.LevelOf(Readiness) : 0;

    private float Tempo => Readiness != null ? ReadinessUtility.TempoOf(Readiness) : 1f;

    public override void PostMapGenerate()
    {
        base.PostMapGenerate();
        ScheduleNext();
    }

    /// <summary>
    /// 排下一次巡邏：在 [MBTTime, 上次間隔的 25~75%] 之間隨機，再乘上等級節奏——巡邏會一波比一波快。
    /// Schedules the next patrol between MBTTime and 25~75% of the previous gap, times the level tempo, so patrols
    /// come faster and faster.
    /// </summary>
    private void ScheduleNext(int forcedTicks = 0)
    {
        int min = Mathf.Max(Props.MBTTime, 1);
        if (latestPatrolTick < 0) latestPatrolTick = Props.firstPatrolMaxTicks;
        int max = Mathf.Max(latestPatrolTick, min + 1);
        int baseTicks = forcedTicks > 0 ? forcedTicks : new IntRange(min, max).RandomInRange;
        int scaled = Mathf.Max(Mathf.RoundToInt(baseTicks * Tempo), min);
        nextPatrolTick = Find.TickManager.TicksGame + scaled;
        timeToSpawn = scaled;
        latestPatrolTick = Mathf.RoundToInt(Mathf.Clamp(timeToSpawn * Rand.Range(0.25f, 0.75f), min, timeToSpawn));
    }

    public override void CompTick()
    {
        if (!ParentHasMap || !parent.IsHashIntervalTick(SampleInterval)) return;
        if (nextPatrolTick <= 0)
        {
            ScheduleNext();
            return;
        }
        if (Find.TickManager.TicksGame < nextPatrolTick) return;
        SpawnPatrol();
        ScheduleNext();
    }

    public void SpawnPatrol()
    {
        Map map = (parent as MapParent)?.Map;
        Faction faction = PatrolFaction;
        if (map == null || faction == null) return;
        if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, CellFinder.EdgeRoadChance_Hostile)) return;

        float tempo = Tempo;
        int level = Level;
        List<Pawn> group = new List<Pawn>();
        int count = Mathf.Max(1, Mathf.RoundToInt(Rand.Gaussian(Props.averagePawnCount - 1, Props.spawnCountSigma / tempo) + 1));
        AddPawns(group, Props.possibleGeneratePawn, count, faction, map, entry);
        foreach (ReadinessPatrolTier tier in Props.AllTiers())
        {
            if (level < tier.minLevel || tier.options.NullOrEmpty()) continue;
            AddPawns(group, tier.options, Mathf.CeilToInt(tier.count / tempo), faction, map, entry);
        }
        if (group.Count == 0) return;

        // 只併入同派系的 StageThenAttack，不會把巡邏隊塞進別派系的襲擊。Only join our own faction's staging lord.
        Lord lord = map.lordManager.lords.Find(l => l.LordJob is LordJob_StageThenAttack && l.faction == faction);
        if (lord == null) LordMaker.MakeNewLord(faction, new LordJob_StageThenAttack(faction, map.Center, Rand.Range(1, 10)), map, group);
        else lord.AddPawns(group);

        bool first = !greetTriggered;
        greetTriggered = true;
        if (!first)
        {
            Find.LetterStack.ReceiveLetter(ReadinessUtility.KeyOrText(Props.letterLabel, "FFF_ReadinessPatrol_Label").Formatted(faction.NameColored),
                ReadinessUtility.KeyOrText(Props.letterText, "FFF_ReadinessPatrol_Text").Formatted(faction.NameColored),
                LetterDefOf.ThreatSmall, new LookTargets(group), faction);
        }
        Readiness?.Worker.OnPatrolArrived(map, group, first, 1f / Props.patrolCountToRaiseLevel);
    }

    private static void AddPawns(List<Pawn> group, List<PawnGenOption> options, int count, Faction faction, Map map, IntVec3 entry)
    {
        if (options.NullOrEmpty()) return;
        for (int i = 0; i < count; i++)
        {
            PawnGenOption option = options.RandomElementByWeight(o => o.selectionWeight);
            if (option?.kind == null) continue;
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(option.kind, faction, PawnGenerationContext.NonPlayer, tile: map.Tile));
            group.Add((Pawn)GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(entry, map, 6), map, Rot4.Random));
        }
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref nextPatrolTick, "nextPatrolTick", 0);
        Scribe_Values.Look(ref timeToSpawn, "timeToSpawn", 0);
        Scribe_Values.Look(ref greetTriggered, "greetTriggered", false);
        Scribe_Values.Look(ref latestPatrolTick, "latestPatrolTick", -1);
        if (Scribe.mode == LoadSaveMode.PostLoadInit && nextPatrolTick <= 0 && ParentHasMap)
        {
            // 舊存檔只有相對 tick：換算成絕對時間點，避免一讀檔就立刻出巡。Old saves only had relative ticks.
            ScheduleNext(Mathf.Max(timeToSpawn, Props.MBTTime));
        }
    }
}
