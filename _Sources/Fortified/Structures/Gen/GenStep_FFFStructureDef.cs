// 当白昼倾坠之时
using System.Linq;
using System.Collections.Generic;
using RimWorld;
using Verse;
using RimWorld.Planet;

namespace Fortified.Structures
{
    public class GenStep_FFFStructureDef : GenStep
    {
        public GenStep_FFFStructureDef() { }

        public override int SeedPart => 394857327;

        public List<FFF_StructureDef> structureDefs;

        public FactionDef forcedFaction;

        public RotEnum allowedRotation = RotEnum.North;

        public override void Generate(Map map, GenStepParams parms)
        {
            if(structureDefs.NullOrEmpty()) return;
            Faction faction = null;
			if (forcedFaction != null)
            {
				faction = Find.FactionManager.FirstFactionOfDef(forcedFaction);
            }
            if(faction == null)
            {
                faction = map.ParentFaction ?? parms.sitePart?.site?.Faction;
			}
			FFF_StructureDef def = structureDefs.RandomElement();
            Rot4 rot = allowedRotation.Random();
            if(CellRect.WholeMap(map).ContractedBy(5).TryFindRandomInnerRect(def.GetSize(rot), out var rect))
            {
				FFF_StructureUtility.Generate(def, rect.CenterCell, map, faction, rot, reconnectPower: false);
			}
        }
    }
}
