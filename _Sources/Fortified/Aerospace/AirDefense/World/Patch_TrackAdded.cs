using Fortified;
using HarmonyLib;
using RimWorld.Planet;

namespace Fortified
{
    /// <summary>
    /// 世界雷達網平時不掃描（<see cref="WorldComponent_AirDefenseNetwork.PresenceCheckIntervalTicks"/>）。航跡一加入世界就通知它，
    /// 剛發射的導彈不必等到下一次長週期檢查才被看見；網路只替這一條航跡啟動航線上的節點，齊射不會每枚都重做完整檢查。
    /// 由 <see cref="AirTrackProvider"/> 提供的物件不是航跡型別，等長週期檢查，或由提供者自己呼叫
    /// <see cref="WorldComponent_AirDefenseNetwork.NotifyTrackChanged()"/>。
    /// </summary>
    [HarmonyPatch(typeof(WorldObjectsHolder), nameof(WorldObjectsHolder.Add))]
    [HarmonyPatchCategory(FFF_AerospaceFeatures.MissilePatches)]
    public static class Patch_TrackAdded
    {
        public static void Postfix(WorldObject o)
        {
            if (o is IAirTrack) { WorldComponent_AirDefenseNetwork.Current?.NotifyTrackChanged(o); }
            // 新據點可能是防空節點：完整節點清單下次讀取時重建。
            else if (o is MapParent) { WorldComponent_AirDefenseNetwork.Current?.MarkNodesDirty(); }
        }
    }
}
