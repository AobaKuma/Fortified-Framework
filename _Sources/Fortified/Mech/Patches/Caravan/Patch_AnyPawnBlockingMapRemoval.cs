using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Fortified
{
	/// <summary>
	/// 玩家能帶隊的機兵（ICaravanOwner.CanCaravan，例如監督者機兵）跟殖民者一樣留住地圖。
	/// 只算玩家派系：敵方或封存艙裡休眠的同型機兵（例如 DMS 地下儲藏庫的 Sergeant）不能讓站點地圖永遠移除不了。
	/// A player mech that can lead a caravan (ICaravanOwner.CanCaravan, e.g. an overseer mech) keeps the map like a
	/// colonist does. Player faction only: a hostile or capsule-dormant one of the same class (say a Sergeant in a DMS
	/// storage vault) must not keep a site map from ever being removed.
	/// </summary>
	[HarmonyPatch(typeof(MapPawns), nameof(MapPawns.AnyPawnBlockingMapRemoval), MethodType.Getter)]
	public class Patch_AnyPawnBlockingMapRemoval
	{
		[HarmonyPostfix]
		public static void Postfix(ref bool __result, MapPawns __instance)
		{
			if (__result) return;
			Faction player = Faction.OfPlayer;
			foreach (Pawn item in __instance.AllPawns)
			{
				if (item.Faction == player && item is ICaravanOwner owner && owner.CanCaravan)
				{
					__result = true;
					return;
				}
			}
		}
	}
}
