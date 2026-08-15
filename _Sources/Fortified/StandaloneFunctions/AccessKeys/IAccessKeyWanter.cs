using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{
	/// <summary>
	/// 需要門禁鑰匙的一端。由 <see cref="Thing"/> 本體或其任一 <see cref="ThingComp"/> 實作皆可。
	/// 兩個回呼一律由 <see cref="AccessKeyLinkUtility"/> 觸發，參數型別為
	/// <see cref="IAccessKeyActivatable"/> 而非具體 Comp，因此可對接任何 activatable 實作。
	/// </summary>
	public interface IAccessKeyWanter
	{
		/// <summary>鑰匙被使用時觸發。<paramref name="pawn"/> 可能為 null（非 Pawn 觸發）。</summary>
		void Notify_AccessKeyUsed(IAccessKeyActivatable activatable, Pawn pawn = null);

		/// <summary>連結建立時觸發。同一 wanter 可能被多個 activatable 連結，需自行累計。</summary>
		void Notify_LinkedTo(IAccessKeyActivatable activatable);
	}
}
