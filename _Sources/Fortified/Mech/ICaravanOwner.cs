using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fortified
{
	public interface ICaravanOwner
	{
		bool CanCaravan { get; }//If true pawn can own and lead caravan, if false pawn will still gather resources and prepare caravan but won't be able to control caravan
	}
}
