using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Fortified
{
    [HarmonyPatch(typeof(MechanitorUtility), "InMechanitorCommandRange")]
    internal class Patch_InMechanitorCommandRange
    {
        private static void Postfix(Pawn mech, LocalTargetInfo target, ref bool __result)
        {
            if (__result) return;
            if (mech == null) return;

            //This region should on average be processed faster than comp processing so put it above.
            //IMPORTANT: it must never early-return when there is no overseer. Mechs without an
            //overseer (woken CompDeadManSwitch mechs, drones, relays) are exactly the cases the
            //checks further down are meant to cover - bailing out here left them with no
            //commandable area at all.
            #region Overseer
            //狀態可控機械（IOverseer / AMO…）：單一介面檢查。
            if (mech is IStateControllableMech sc && sc.ControllableByState)
            {
                __result = true;
                return;
            }
            Pawn overseer = mech.GetOverseer();
            if (overseer != null)
            {
                Thing overlord = overseer.GetOverseerThing(out var overseerInt);
                if (overlord != null
                    && overlord.MapHeld != null && overlord.MapHeld == mech.MapHeld
                    && (target.IsValid && overlord.PositionHeld.DistanceTo(target.Cell) <= overseerInt.Comp.CurrentCommandRange))
                {
                    __result = true;
                    return;
                }
            }
            #endregion

            //Woken mechs answer to nobody, so they are never out of command range.
            if (mech is ICachedMechComps cc1 && cc1.DeadManSwitchComp?.woken == true)
            {
                __result = true;
                return;
            }
            if (mech is ICachedMechComps cc2 && cc2.CommandRelayComp != null)
            {
                __result = true;
                return;
            }
            if (mech is ICachedMechComps cc3 && cc3.DroneComp != null)
            {
                __result = true;
                return;
            }

            //Everything below resolves relays through the mech's overseer, so it needs one.
            if (overseer == null) return;

            var relays = CompCommandRelay.allRelays;
            if (relays != null)
            {
                for (int i = 0; i < relays.Count; i++)
                {
                    CompCommandRelay relay = relays[i];
                    if (relay == null || relay.parent is not Pawn relayPawn) continue;
                    if (relayPawn.Spawned && relayPawn.MapHeld == mech.MapHeld && relayPawn.GetOverseer() == overseer)
                    {
                        if (relay.Props.coverWholeMap || CheckUtility.InRange(relayPawn.Position, target, relay.SquaredDistance))
                        {
                            __result = true;
                            return;
                        }
                    }
                }
            }

            if (CheckUtility.HasSubRelayInMapAndInbound(mech, target))
            {
                __result = true;
                return;
            }
        }
    }
}
