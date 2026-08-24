using UnityEngine;
using Verse;

namespace Fortified
{
	/// <summary>
	/// 人型機械標記：凡具備 HumanlikeMech 的人型初始化/渲染/服裝支援的 Pawn
	/// 皆實作此介面（HumanlikeMech、ArtificialOrganismHumanlike…）。
	/// 取代散落在渲染/服裝/UI patch 中的 <c>is HumanlikeMech</c> 硬編碼型別檢查。
	/// </summary>
	public interface IHumanlikeMech
	{
		HumanlikeMechExtension Extension { get; }

		Graphic HeadGraphic { get; }

		/// <summary>限制機械體工作類型（HumanlikeMech / ArtificialOrganismHumanlike 皆實作）。</summary>
		void ApplyWorkTypeRestrictions();
	}
}
