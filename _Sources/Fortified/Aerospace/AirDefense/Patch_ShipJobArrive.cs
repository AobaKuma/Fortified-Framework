using HarmonyLib;
using RimWorld;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 攔截玩家穿梭機的降落：原版穿梭機由 <see cref="ShipJob_Arrive"/> 以 GenSpawn 生成 skyfaller，
    /// 不經過 <see cref="Patch_MakeDropPodAt"/>，所以在這裡把它收進 <see cref="MapComponent_AirDefense"/>。
    /// 未被攔截時 AirDefenseTarget.Resolve 重新生成 skyfaller，穿梭機照常落地、ShipJob_Arrive 正常結束；
    /// 被攔截時 AirDefenseTarget.Discard 會結束並處置運輸船。
    /// </summary>
    [HarmonyPatch(typeof(ShipJob_Arrive), nameof(ShipJob_Arrive.TryStart))]
    [HarmonyPatchCategory(FFF_AerospaceFeatures.MissilePatches)]
    public static class Patch_ShipJobArrive
    {
        [HarmonyPostfix]
        public static void Postfix(ShipJob_Arrive __instance, bool __result)
        {
            if (!__result) { return; }
            Thing ship = __instance.transportShip?.shipThing;
            if (ship == null || ship.Faction == null || !ship.Faction.IsPlayer) { return; }
            if (!(ship.ParentHolder is Skyfaller incoming) || !incoming.Spawned) { return; }

            Map map = incoming.Map;
            MapComponent_AirDefense comp = map.GetComponent<MapComponent_AirDefense>();
            Faction defender = comp?.ResolveDefender(ship.Faction);
            if (defender == null) { return; }

            IntVec3 pos = incoming.Position;
            incoming.DeSpawn();
            comp.RegisterIncoming(incoming, pos, defender);

            if (Prefs.DevMode)
            {
                Log.Message($"[Fortified BVR] {defender.Name} 攔截玩家穿梭機：{ship.LabelCap} @ {pos}");
            }
        }
    }
}
