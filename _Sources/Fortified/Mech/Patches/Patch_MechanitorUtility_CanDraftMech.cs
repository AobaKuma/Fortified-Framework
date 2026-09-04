using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Fortified
{
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.CanDraftMech), MethodType.Normal)]
    internal static class Patch_MechanitorUtility_CanDraftMech
    {
        static void Postfix(Pawn mech, ref AcceptanceReport __result)
        {
            if (__result == true) return;
			//狀態可控機械（IOverseer / AMO…）：單一介面檢查。
			if (mech is IStateControllableMech sc && sc.ControllableByState)
			{
				__result = true;
                return;
			}
			if (mech.DeadOrDowned) return;
			if ((!mech.IsColonyMech && mech.HostFaction == null)) return;

            //woken DMS 機械走快取 comps。
            if (mech.CachedDeadManSwitch()?.woken == true)
            {
                __result = true;
            }
            else if (mech.HostFaction == Faction.OfPlayer)
            {
                __result = true;
            }

            //快取 comps（spawn 後有效）；另保留 def 層 HasComp 以涵蓋 spawn 前語境（行為等價）。
            if (mech.CachedCommandRelay() != null
                || mech.kindDef.race.HasComp(typeof(CompCommandRelay)))
            {
                __result = true;
                return;
            }
            Pawn overseer = MechanitorUtility.GetOverseer(mech);
            if (overseer == null) return;

            var relays = CompCommandRelay.allRelays;
            for (int i = 0; i < relays.Count; i++)
            {
                CompCommandRelay relay = relays[i];
                Pawn relayPawn = (Pawn)relay.parent;
                if (relayPawn.Spawned && relayPawn.MapHeld == mech.MapHeld && relayPawn.GetOverseer() == overseer)
                {
                    __result = true;
                    return;
                }
            }
        }
    }
}
