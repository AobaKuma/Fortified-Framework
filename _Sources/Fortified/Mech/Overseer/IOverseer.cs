using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{
	public interface IOverseer
	{
		CompOverseer Comp { get; }
	}

	public interface IOverseerMech : IOverseer
	{
		float MinCharge { get; set; }

		float MaxCharge { get; set; }

		MechWorkModeDef WorkMode { get; set; }

		void Notify_NameChanged();
	}
}
