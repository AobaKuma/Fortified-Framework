using System.Collections.Generic;
using Verse;

namespace Fortified
{
	/// <summary>
	/// 門禁連結的統一入口。所有「找 activatable / 找 wanter / 建立連結 / 發出通知」
	/// 都應走這裡，避免各處各自 <c>TryGetComp&lt;CompAccessKeyActivatable&gt;()</c> 造成行為不一致。
	/// 兩端皆同時掃描 Thing 本體與其 AllComps。
	/// </summary>
	public static class AccessKeyLinkUtility
	{
		// ---------- Activatable 端 ----------

		/// <summary>取得 thing 上的 activatable（先看 Thing 本體，再看各 Comp）。找不到回傳 null。</summary>
		public static IAccessKeyActivatable GetActivatable(Thing thing)
		{
			if (thing == null || thing.Destroyed)
			{
				return null;
			}
			if (thing is IAccessKeyActivatable selfActivatable)
			{
				return selfActivatable;
			}
			if (thing is ThingWithComps twc && twc.AllComps != null)
			{
				List<ThingComp> comps = twc.AllComps;
				for (int i = 0; i < comps.Count; i++)
				{
					if (comps[i] is IAccessKeyActivatable compActivatable)
					{
						return compActivatable;
					}
				}
			}
			return null;
		}

		/// <summary>取得該格上第一個 activatable。找不到回傳 null。</summary>
		public static IAccessKeyActivatable GetActivatableAt(IntVec3 cell, Map map)
		{
			if (map == null || !cell.IsValid || !cell.InBounds(map))
			{
				return null;
			}
			List<Thing> things = cell.GetThingList(map);
			for (int i = 0; i < things.Count; i++)
			{
				IAccessKeyActivatable activatable = GetActivatable(things[i]);
				if (activatable != null)
				{
					return activatable;
				}
			}
			return null;
		}

		public static bool HasActivatableAt(IntVec3 cell, Map map)
		{
			return GetActivatableAt(cell, map) != null;
		}

		// ---------- Wanter 端 ----------

		/// <summary>thing 本體或其任一 Comp 是否為 wanter。</summary>
		public static bool IsWanter(Thing thing)
		{
			if (thing == null || thing.Destroyed)
			{
				return false;
			}
			if (thing is IAccessKeyWanter)
			{
				return true;
			}
			if (thing is ThingWithComps twc && twc.AllComps != null)
			{
				List<ThingComp> comps = twc.AllComps;
				for (int i = 0; i < comps.Count; i++)
				{
					if (comps[i] is IAccessKeyWanter)
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>取得該格上第一個 wanter Thing。找不到回傳 null。</summary>
		public static Thing GetWanterAt(IntVec3 cell, Map map)
		{
			if (map == null || !cell.IsValid || !cell.InBounds(map))
			{
				return null;
			}
			List<Thing> things = cell.GetThingList(map);
			for (int i = 0; i < things.Count; i++)
			{
				if (IsWanter(things[i]))
				{
					return things[i];
				}
			}
			return null;
		}

		public static bool HasWanterAt(IntVec3 cell, Map map)
		{
			return GetWanterAt(cell, map) != null;
		}

		/// <summary>列出 thing 上所有 wanter 實作（Thing 本體 + 各 Comp）。</summary>
		public static List<IAccessKeyWanter> GetWanters(Thing thing)
		{
			List<IAccessKeyWanter> result = new List<IAccessKeyWanter>();
			if (thing == null || thing.Destroyed)
			{
				return result;
			}
			if (thing is IAccessKeyWanter selfWanter)
			{
				result.Add(selfWanter);
			}
			if (thing is ThingWithComps twc && twc.AllComps != null)
			{
				List<ThingComp> comps = twc.AllComps;
				for (int i = 0; i < comps.Count; i++)
				{
					if (comps[i] is IAccessKeyWanter compWanter)
					{
						result.Add(compWanter);
					}
				}
			}
			return result;
		}

		// ---------- 連結 / 通知 ----------

		/// <summary>
		/// 建立連結：設定 <see cref="IAccessKeyActivatable.LinkedAccessWanter"/>，
		/// 再依序通知 activatable 與 wanter 上的所有實作。
		/// 任一前置條件不成立即回傳 false 且不產生任何副作用。
		/// </summary>
		public static bool TryLink(IAccessKeyActivatable activatable, Thing wanter)
		{
			if (activatable == null || wanter == null || wanter.Destroyed)
			{
				return false;
			}
			if (activatable.AccessKeyActivated)
			{
				return false;
			}

			Thing activatableThing = activatable.ParentThing;
			if (activatableThing == null || activatableThing.Destroyed || activatableThing == wanter)
			{
				return false;
			}

			List<IAccessKeyWanter> wanters = GetWanters(wanter);
			if (wanters.Count == 0)
			{
				return false;
			}
			if (!activatable.CanLinkWanter(wanter))
			{
				return false;
			}

			activatable.LinkedAccessWanter = wanter;
			activatable.Notify_WanterLinked(wanter);
			for (int i = 0; i < wanters.Count; i++)
			{
				wanters[i].Notify_LinkedTo(activatable);
			}
			return true;
		}

		/// <summary>
		/// 在該格尋找 activatable 並與 wanter 建立連結。供生成流程使用。
		/// </summary>
		public static bool TryLinkAt(IntVec3 activatableCell, IntVec3 wanterCell, Map map)
		{
			if (map == null)
			{
				return false;
			}
			IAccessKeyActivatable activatable = GetActivatableAt(activatableCell, map);
			if (activatable == null)
			{
				return false;
			}
			Thing wanter = GetWanterAt(wanterCell, map);
			if (wanter == null)
			{
				return false;
			}
			return TryLink(activatable, wanter);
		}

		/// <summary>鑰匙被使用時，通知已連結 wanter 上的所有實作。</summary>
		public static void NotifyAccessKeyUsed(IAccessKeyActivatable activatable, Pawn pawn = null)
		{
			if (activatable == null)
			{
				return;
			}
			Thing wanter = activatable.LinkedAccessWanter;
			if (wanter == null || wanter.Destroyed)
			{
				return;
			}
			List<IAccessKeyWanter> wanters = GetWanters(wanter);
			for (int i = 0; i < wanters.Count; i++)
			{
				wanters[i].Notify_AccessKeyUsed(activatable, pawn);
			}
		}
	}
}
