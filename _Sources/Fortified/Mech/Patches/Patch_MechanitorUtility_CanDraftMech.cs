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
			if (mech is IOverseer)
			{
				__result = true;
                return;
			}
			if (mech.DeadOrDowned) return;
			if ((!mech.IsColonyMech && mech.HostFaction == null)) return;

            if (mech.TryGetComp<CompDeadManSwitch>() is CompDeadManSwitch comp && comp.woken)
            {
                __result = true;
            }
            else if (mech.HostFaction == Faction.OfPlayer)
            {
                __result = true;
            }

            if (mech.kindDef.race.HasComp(typeof(CompCommandRelay)))
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
