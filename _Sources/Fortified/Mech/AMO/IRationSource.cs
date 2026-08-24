using Verse;

namespace Fortified
{
	/// <summary>
	/// 口糧來源抽象：AMO 體內的口糧艙（如 DMS.CompRationMagazine）實作此介面，
	/// 讓 Framework 的 CompArtificialOrganism 不依賴特定模組的 comp 型別。
	/// </summary>
	public interface IRationSource
	{
		ThingDef RationDef { get; }

		int LoadedRations { get; }

		int MaxRations { get; }

		/// <summary>消耗 count 份口糧；不足時回傳 false 且不消耗。</summary>
		bool TryConsume(int count);
	}
}
