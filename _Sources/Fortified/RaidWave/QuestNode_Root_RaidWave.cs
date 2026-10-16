using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace Fortified;

/// <summary>
/// Root quest node for a RaidWave. Reads 'raidwave', 'map', and 'wave' from the Slate,
/// generates pawns, schedules their arrival via QuestPart_RaidWaveArrives, drops them in,
/// and sets up the assault lord via QuestPart_RaidWave.
/// </summary>
public class QuestNode_Root_RaidWave : QuestNode
{
    private static readonly IntRange MinDelayRange = new IntRange(2500, 5000);
    private static readonly IntRange MaxDelayRange = new IntRange(60000, 180000);

    protected override void RunInt()
    {
        Slate slate = QuestGen.slate;
        Quest quest = QuestGen.quest;

        RaidWaveDef raidWaveDef = slate.Get<RaidWaveDef>("raidwave");
        Map map = slate.Get<Map>("map");
        int timesCalled = slate.Get("wave", 0);

        if (raidWaveDef == null || map == null)
        {
            Log.Error("[FortifiedFramework] QuestNode_Root_RaidWave: missing 'raidwave' or 'map' in slate.");
            return;
        }

        // --- Resolve faction ---
        Faction faction = Find.FactionManager.FirstFactionOfDef(raidWaveDef.factionDef);
        if (faction == null)
        {
            // Create a temporary hostile faction if none exists on this world
            List<FactionRelation> relations = Find.FactionManager.AllFactionsListForReading
                .Select(f => new FactionRelation { other = f, kind = FactionRelationKind.Hostile })
                .ToList();
            faction = FactionGenerator.NewGeneratedFactionWithRelations(
                new FactionGeneratorParms(raidWaveDef.factionDef, default, hidden: true),
                relations);
            faction.temporary = true;
            Find.FactionManager.Add(faction);
        }

        // --- Generate pawns for the current wave ---
        RaidWave wave = raidWaveDef.GetWave(timesCalled);
        List<Pawn> allPawns = new List<Pawn>();

        foreach (PawnKindDefCount entry in wave.pawns)
        {
            if (entry.kindDef == null || entry.count <= 0) continue;

            // Temporary factions are removed by FactionManager once the quest is cleaned up,
            // but RoyalTitle / FactionPermit entries on surviving pawns (and corpses) keep a
            // reference to them. That reference resolves to null on the next load and
            // Pawn_RoyaltyTracker.ExposeData then spams "Cannot get current title for null
            // faction" / throws in AssignHeirIfNone. Never hand out titles for temp factions.
            PawnGenerationRequest request = new PawnGenerationRequest(
                entry.kindDef, faction,
                PawnGenerationContext.NonPlayer,
                tile: -1,
                forceGenerateNewPawn: true,
                forbidAnyTitle: faction.temporary);

            for (int i = 0; i < entry.count; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                Find.WorldPawns.PassToWorld(pawn);
                allPawns.Add(pawn);
            }
        }

        if (allPawns.Count == 0)
        {
            Log.Warning($"[FortifiedFramework] QuestNode_Root_RaidWave: wave {timesCalled} of {raidWaveDef.defName} produced no pawns. Check PawnKindDefCount entries.");
            return;
        }

        // Ensure pawns appear in attack target cache immediately
        foreach (Pawn pawn in allPawns)
            map.attackTargetsCache.UpdateTarget(pawn);

        // --- Arrival signal ---
        string arrivalSignal = QuestGen.GenerateNewSignal("RaidWaveArrives");

        // --- Schedule arrival ---
        QuestPart_RaidWaveArrives arrivesPart = new QuestPart_RaidWaveArrives
        {
            mapParent = map.Parent,
            raidWaveDef = raidWaveDef,
            minDelay = MinDelayRange.RandomInRange,
            maxDelay = MaxDelayRange.RandomInRange,
            inSignalEnable = slate.Get<string>("inSignal"),
        };
        arrivesPart.outSignalsCompleted.Add(arrivalSignal);
        quest.AddPart(arrivesPart);

        // --- Drop pods (sendStandardLetter: false — we send our own letter below) ---
        IntVec3 dropCenter = DropCellFinder.FindRaidDropCenterDistant(map);
        quest.DropPods(
            mapParent: map.Parent,
            contents: allPawns,
            sendStandardLetter: false,
            joinPlayer: false,
            makePrisoners: false,
            inSignal: arrivalSignal,
            signalListenMode: QuestPart.SignalListenMode.OngoingOnly,
            dropSpot: dropCenter,
            destroyItemsOnCleanup: true,
            dropAllInSamePod: false,
            allowFogged: false,
            canRetargetAnyMap: false,
            faction: faction);

        // --- Threat letter when they land ---
        quest.Letter(
            letterDef: LetterDefOf.ThreatBig,
            inSignal: arrivalSignal,
            relatedFaction: faction,
            signalListenMode: QuestPart.SignalListenMode.OngoingOnly,
            lookTargets: allPawns.Cast<object>(),
            text: "FFF_RaidWave_LetterText".Translate(faction.NameColored),
            label: "FFF_RaidWave_LetterLabel".Translate());

        // --- Pre-arrival alert (shown while in transit) ---
        quest.Alert(
            label: "FFF_RaidWave_Alert".Translate(),
            explanation: "FFF_RaidWave_AlertDesc".Translate(faction.NameColored),
            critical: true,
            getLookTargetsFromSignal: false,
            inSignalDisable: arrivalSignal);

        // --- Lord / assault setup (activates on the same arrival signal as DropPods) ---
        QuestPart_RaidWave lordPart = new QuestPart_RaidWave
        {
            faction = faction,
            mapParent = map.Parent,
            inSignal = arrivalSignal,
        };
        lordPart.pawns.AddRange(allPawns);
        quest.AddPart(lordPart);

        // --- End quest when all pawns are dead ---
        // AnyPawnAlive fires outSignalElse when none of the tracked pawns remain alive.
        string allDeadSignal = QuestGen.GenerateNewSignal("AllRaidersDead");
        quest.AnyPawnAlive(
            pawns: allPawns,
            outSignalElse: allDeadSignal);
        quest.End(QuestEndOutcome.Unknown, 0, null, allDeadSignal);

        // --- End quest if map is destroyed ---
        // The "mapParent.Destroyed" signal only fires if 'mapParent' is a slate var: QuestGen tags
        // slate objects named in hardcoded signals when generation finishes (same as vanilla Bossgroup).
        slate.Set("mapParent", map.Parent);
        quest.End(QuestEndOutcome.Unknown, 0, null,
            QuestGenUtility.HardcodedSignalWithQuestID("mapParent.Destroyed"));
    }

    protected override bool TestRunInt(Slate slate)
    {
        return slate.Exists("raidwave") && slate.Exists("map") && slate.Exists("wave");
    }
}
