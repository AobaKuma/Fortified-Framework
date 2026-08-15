using Verse;

namespace Fortified
{
	/// <summary>
	/// 可被門禁鑰匙啟動的一端。
	/// 由 <see cref="Thing"/> 本體或其任一 <see cref="ThingComp"/> 實作皆可，
	/// 連結流程一律透過 <see cref="AccessKeyLinkUtility"/> 以本介面操作，
	/// 不直接依賴 <see cref="CompAccessKeyActivatable"/>。
	/// </summary>
	public interface IAccessKeyActivatable
	{
		/// <summary>
		/// 承載此 activatable 的 Thing。由 ThingComp 實作時應回傳 parent；
		/// 由 Thing 本身實作時回傳 this。不可回傳 null。
		/// </summary>
		Thing ParentThing { get; }

		/// <summary>
		/// 目前連結的 wanter。setter 需自行處理存檔（Scribe_References）。
		/// </summary>
		Thing LinkedAccessWanter { get; set; }

		/// <summary>
		/// 是否已被啟動。已啟動者不應再被重新連結。
		/// </summary>
		bool AccessKeyActivated { get; }

		/// <summary>
		/// 是否允許與指定 wanter 建立連結。回傳 false 則 <see cref="AccessKeyLinkUtility.TryLink"/> 失敗。
		/// 實作方可在此加上距離、派系、型別等額外限制。
		/// </summary>
		bool CanLinkWanter(Thing wanter);

		/// <summary>
		/// 連結成功後的回呼，發生在 <see cref="LinkedAccessWanter"/> 已被設定之後。
		/// </summary>
		void Notify_WanterLinked(Thing wanter);
	}
}
