using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{
	public interface IAccessKeyWanter
	{
		public void Notify_AccessKeyUsed(CompAccessKeyActivatable comp, Pawn pawn = null);

		public void Notify_LinkedTo(CompAccessKeyActivatable comp);
	}
}
