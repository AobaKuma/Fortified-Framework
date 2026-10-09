using HarmonyLib;
using RimWorld;
using Verse;

namespace Fortified;

// 原版在重力船降落與地圖移除時都會呼叫 Scenario 的這兩個方法，與開局內容無關，
// 所以由任務或程式碼啟動的追緝也能跟著重力船、並在地圖消失時清除。
// Vanilla calls these Scenario methods on every gravship landing and map removal regardless of the
// scenario's parts, so huntdowns started by quests or code also follow the gravship and get cleaned up.

[HarmonyPatch(typeof(Scenario), nameof(Scenario.PostGravshipLanded))]
public static class Patch_Huntdown_PostGravshipLanded
{
    public static void Postfix(Map map)
    {
        HuntdownUtility.Notify_GravshipLanded(map);
    }
}

[HarmonyPatch(typeof(Scenario), nameof(Scenario.MapRemoved))]
public static class Patch_Huntdown_MapRemoved
{
    public static void Postfix(Map map)
    {
        HuntdownUtility.Notify_MapRemoved(map);
    }
}
