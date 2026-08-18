using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{
	public class CompProperties_OverseerFacility : CompProperties_Facility
	{
		public CompProperties_OverseerFacility()
		{
			compClass = typeof(CompOverseerFacility);
		}

		public override IEnumerable<StatDrawEntry> SpecialDisplayStats(StatRequest req)
		{
			if (statOffsets == null)
			{
				yield break;
			}
			foreach (StatModifier statOffset in statOffsets)
			{
				yield return new StatDrawEntry(statOffset.stat.category, statOffset.stat.OffsetLabelCap, statOffset.value.ToStringByStyle(statOffset.stat.toStringStyle, ToStringNumberSense.Offset), statOffset.stat.description, statOffset.stat.displayPriorityInCategory);
			}
		}
	}

	public class CompOverseerFacility : CompFacility
	{
		public override void PostSpawnSetup(bool respawningAfterReload)
		{
			base.OnLinkAdded += Notify_LinkAdded;
			base.OnLinkRemoved += Notify_LinkRemoved;
			base.PostSpawnSetup(respawningAfterReload);
		}

		public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
		{
			base.PostDeSpawn(map, mode);
			base.OnLinkAdded -= Notify_LinkAdded;
			base.OnLinkRemoved -= Notify_LinkRemoved;
		}

		protected virtual void Notify_LinkAdded(CompFacility facility, Thing thing)
		{
			if (thing is IOverseer overseer)
			{
				overseer.Comp.Notify_BandwidthChanged();
			}
		}

		protected virtual void Notify_LinkRemoved(CompFacility facility, Thing thing)
		{
			if (thing is IOverseer overseer)
			{
				overseer.Comp.Notify_BandwidthChanged();
			}
		}

		public override void ReceiveCompSignal(string signal)
		{
			base.ReceiveCompSignal(signal);
			if (signal == "PowerTurnedOff" || signal == "PowerTurnedOn")
			{
				foreach(Thing t in LinkedBuildings)
				{
					if (t is IOverseer overseer)
					{
						overseer.Comp.Notify_BandwidthChanged();
					}
				}
			}
		}
	}
}
