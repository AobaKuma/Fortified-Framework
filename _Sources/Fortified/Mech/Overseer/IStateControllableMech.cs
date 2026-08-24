using Verse;

namespace Fortified
{
	/// <summary>
	/// 依狀態被控制：實作者宣告自己在當前狀態下「是否可直接受玩家控制」。
	/// 取代散落在各 Harmony patch 的 TryGetComp 判定，成為可控性檢查的單一入口。
	/// </summary>
	public interface IStateControllableMech
	{
		/// <summary>目前是否可直接受控制（不需機械師、不需範圍、不需頻寬檢查）。</summary>
		bool ControllableByState { get; }
	}
}
