using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Fortified;

/// <summary>
/// 開局即被追緝。實際邏輯全在 <see cref="HuntdownUtility"/>，這裡只在開局地圖生成時啟動。
/// Starts a huntdown with the game. All logic lives in <see cref="HuntdownUtility"/>; this only starts it.
/// <code>
/// &lt;li Class="Fortified.ScenPart_Huntdown"&gt;
///   &lt;def&gt;FFF_Huntdown&lt;/def&gt;
///   &lt;huntdown&gt;MyMod_SomeHuntdown&lt;/huntdown&gt;
///   &lt;bindStartingPawn&gt;true&lt;/bindStartingPawn&gt;
/// &lt;/li&gt;
/// </code>
/// </summary>
public class ScenPart_Huntdown : ScenPart
{
    public HuntdownDef huntdown;

    /// <summary>
    /// 綁定第一名開局角色：他被軍事法庭審判或死亡時追緝結束（見 HuntdownDef.stopWhenBoundPawnLost）。
    /// Binds the first starting pawn: the huntdown ends when they stand trial or die (see stopWhenBoundPawnLost).
    /// </summary>
    public bool bindStartingPawn;

    // 開局地圖尚未生成。The starting map has not been generated yet.
    private bool awaitingStartMap;

    public override bool OverrideDangerMusic => awaitingStartMap;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Defs.Look(ref huntdown, "huntdown");
        Scribe_Values.Look(ref bindStartingPawn, "bindStartingPawn");
        Scribe_Values.Look(ref awaitingStartMap, "awaitingStartMap");
    }

    public override bool HasNullDefs() => base.HasNullDefs() || huntdown == null;

    public override void Randomize()
    {
        huntdown = DefDatabase<HuntdownDef>.AllDefsListForReading.RandomElementWithFallback();
    }

    public override void DoEditInterface(Listing_ScenEdit listing)
    {
        Rect rect = listing.GetScenPartRect(this, RowHeight);
        if (Widgets.ButtonText(rect, huntdown?.LabelCap ?? "-"))
        {
            RimWorld.FloatMenuUtility.MakeMenu(DefDatabase<HuntdownDef>.AllDefsListForReading,
                d => d.LabelCap, d => () => huntdown = d);
        }
    }

    public override string Summary(Scenario scen)
    {
        if (huntdown == null) return null;
        string faction = huntdown.faction?.LabelCap ?? huntdown.LabelCap;
        return "FFF_Huntdown_ScenSummary".Translate(faction).CapitalizeFirst();
    }

    public override void PostWorldGenerate()
    {
        awaitingStartMap = true;
    }

    public override void PostMapGenerate(Map map)
    {
        if (!awaitingStartMap || huntdown == null) return;
        awaitingStartMap = false;
        Pawn boundPawn = bindStartingPawn ? Find.GameInitData?.startingAndOptionalPawns?.FirstOrDefault() : null;
        HuntdownUtility.Start(huntdown, map, initial: true, source: "Scenario", boundPawn: boundPawn);
    }
}
