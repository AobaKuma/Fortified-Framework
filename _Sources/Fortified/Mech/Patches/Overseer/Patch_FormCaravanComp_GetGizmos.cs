using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{
	[HarmonyPatch(typeof(FormCaravanComp), nameof(FormCaravanComp.GetGizmos))]
	public class Patch_FormCaravanComp_GetGizmos
	{
		[HarmonyPostfix]
		public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, FormCaravanComp __instance)
		{
			bool flag = false;
			foreach (Gizmo g in __result)
			{
				if (g is Command_Action action && action.tutorTag == "ReformCaravan")
				{
					flag = true;
				}
				yield return g;
			}
			if (flag)
			{
				yield break;
			}
			MapParent mapParent = (MapParent)__instance.parent;
			if (mapParent.HasMap && __instance.Reform && mapParent.Map.mapPawns.FreeColonistsSpawned.Count == 0 && !__instance.AnyActiveThreatNow && mapParent.Map.mapPawns.PawnsInFaction(Faction.OfPlayerSilentFail).Any((x) => x is ICaravanOwner owner && owner.CanCaravan))
			{
				Command_Action command_Action = new Command_Action();
				command_Action.defaultLabel = "CommandReformCaravan".Translate();
				command_Action.defaultDesc = "CommandReformCaravanDesc".Translate();
				command_Action.icon = FormCaravanComp.FormCaravanCommand;
				command_Action.hotKey = KeyBindingDefOf.Misc2;
				command_Action.tutorTag = "ReformCaravan";
				command_Action.action = delegate
				{
					if (ModsConfig.OdysseyActive && mapParent.Map.listerThings.ThingsOfDef(ThingDefOf.GravEngine).Any())
					{
						Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmLoseGravship".Translate(), Form));
					}
					else if (ModsConfig.OdysseyActive && mapParent.Map.listerThings.ThingsInGroup(ThingRequestGroup.PassengerShuttle).Any())
					{
						Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmLoseShuttle".Translate(), Form));
					}
					else
					{
						Form();
					}
				};
				if (GenHostility.AnyHostileActiveThreatToPlayer(mapParent.Map, countDormantPawnsAsHostile: true))
				{
					command_Action.Disable("CommandReformCaravanFailHostilePawns".Translate());
				}
				yield return command_Action;
			}
			void Form()
			{
				Find.WindowStack.Add(new Dialog_FormCaravan(mapParent.Map, reform: true));
			}
		}
	}
}
