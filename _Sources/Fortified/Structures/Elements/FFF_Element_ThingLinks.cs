// 当白昼倾坠之时
using RimWorld;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace Fortified.Structures
{
    /// <summary>
    /// 可被結構導出／生成保存「物件之間連結」的建築或 Comp（例如船體骨架的結構樁鋼樑）。
    /// Thing 本體或 ThingComp 實作皆可。導出時寫出兩端都在範圍內的連結，生成時在兩端格位找回物件並重建。
    ///
    /// A Thing or ThingComp whose links to other things should survive structure export / generation
    /// (e.g. structural-pile beams of a ship skeleton). Export writes every link whose both ends are inside
    /// the exported rect; generation finds both ends by cell and re-links them.
    /// </summary>
    public interface IFFF_StructureLinkable
    {
        /// <summary>目前已完成的連結對象。Things this one is currently linked to.</summary>
        IEnumerable<Thing> StructureLinks { get; }

        /// <summary>
        /// 生成時重建與 other 的連結（不消耗材料、不走施工）。已連結時直接回傳 true。
        /// Re-create the link to other during generation (no cost, no construction). Return true if linked.
        /// </summary>
        bool TryLinkForStructure(Thing other);
    }

    /// <summary>一條連結的兩端格位（相對結構原點，各為物件的 Position）。Both ends of one link, relative to the structure origin.</summary>
    public class FFF_ThingLinkEntry
    {
        public IntVec3 a;
        public IntVec3 b;
    }

    /// <summary>
    /// 結構元素：生成後把成對的 IFFF_StructureLinkable 連起來。純生成任務，不加東西進 sketch；
    /// 任務在所有建築生成完之後才執行，所以放在元素清單哪裡都可以。
    /// Layout element: link pairs of IFFF_StructureLinkable after generation. Task-only, so its position in
    /// the element list does not matter.
    /// </summary>
    public class FFF_Element_ThingLinks : FFF_Element, IFFF_TaskProvider
    {
        public FFF_Element_ThingLinks() { }
        public List<FFF_ThingLinkEntry> links = new List<FFF_ThingLinkEntry>();

        public override void AddToSketch(Sketch sketch) { }

        public List<IFFF_GenerationTask> GetTasks(Rot4 rot, IntVec3 offset)
        {
            List<IFFF_GenerationTask> tasks = new List<IFFF_GenerationTask>();
            if (links.NullOrEmpty()) return tasks;
            Task_LinkThings task = new Task_LinkThings();
            foreach (FFF_ThingLinkEntry e in links)
            {
                if (e != null) task.pairs.Add(new FFF_ThingLinkEntry { a = e.a, b = e.b });
            }
            tasks.Add(task.Transformed(rot, offset));
            return tasks;
        }
    }

    public class Task_LinkThings : IFFF_GenerationTask
    {
        public List<FFF_ThingLinkEntry> pairs = new List<FFF_ThingLinkEntry>();

        public void Execute(Map map, IntVec3 offset, Faction faction)
        {
            int failed = 0;
            foreach (FFF_ThingLinkEntry pair in pairs)
            {
                Thing a = FFF_StructureLinkUtility.LinkableAt(pair.a + offset, map, out IFFF_StructureLinkable linkA);
                Thing b = FFF_StructureLinkUtility.LinkableAt(pair.b + offset, map, out _);
                if (a == null || b == null || a == b || !linkA.TryLinkForStructure(b))
                {
                    failed++;
                }
            }
            if (failed > 0 && Prefs.DevMode)
            {
                Log.Warning($"[FFF] Task_LinkThings: {failed}/{pairs.Count} link(s) could not be restored.");
            }
        }

        public IFFF_GenerationTask Transformed(Rot4 rot, IntVec3 offset)
        {
            Task_LinkThings t = new Task_LinkThings();
            foreach (FFF_ThingLinkEntry pair in pairs)
            {
                t.pairs.Add(new FFF_ThingLinkEntry { a = pair.a.RotatedBy(rot) + offset, b = pair.b.RotatedBy(rot) + offset });
            }
            return t;
        }
    }

    public static class FFF_StructureLinkUtility
    {
        /// <summary>Thing 本體或其任一 Comp 實作的 IFFF_StructureLinkable。The linkable on the thing itself or one of its comps.</summary>
        public static IFFF_StructureLinkable LinkableOf(Thing thing)
        {
            if (thing is IFFF_StructureLinkable self) return self;
            if (thing is ThingWithComps twc)
            {
                List<ThingComp> comps = twc.AllComps;
                for (int i = 0; i < comps.Count; i++)
                {
                    if (comps[i] is IFFF_StructureLinkable c) return c;
                }
            }
            return null;
        }

        /// <summary>
        /// 依格位找可連結物件。多格建築旋轉後 Position 可能換格，但原 Position 那格仍在它的占地內，所以用占格查。
        /// Find a linkable thing occupying the cell (a rotated multi-cell building still covers its old Position cell).
        /// </summary>
        public static Thing LinkableAt(IntVec3 cell, Map map, out IFFF_StructureLinkable linkable)
        {
            linkable = null;
            if (!cell.InBounds(map)) return null;
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                linkable = LinkableOf(things[i]);
                if (linkable != null) return things[i];
            }
            return null;
        }

        /// <summary>
        /// 導出 things 之間的連結（兩端都在 things 裡才寫）。每條連結只寫一次。
        /// Export links between the given things (both ends must be in the set); each link is written once.
        /// </summary>
        public static void ExportLinks(ICollection<Thing> things, IntVec3 origin, StringBuilder sb)
        {
            StringBuilder links = new StringBuilder();
            HashSet<(Thing, Thing)> written = new HashSet<(Thing, Thing)>();
            foreach (Thing thing in things)
            {
                IFFF_StructureLinkable linkable = LinkableOf(thing);
                if (linkable == null) continue;
                IEnumerable<Thing> targets = linkable.StructureLinks;
                if (targets == null) continue;
                foreach (Thing other in targets)
                {
                    if (other == null || other == thing || !things.Contains(other)) continue;
                    bool forward = thing.thingIDNumber <= other.thingIDNumber;
                    if (!written.Add(forward ? (thing, other) : (other, thing))) continue;
                    IntVec3 a = thing.Position - origin;
                    IntVec3 b = other.Position - origin;
                    links.AppendLine("          <li>");
                    links.AppendLine($"            <a>({a.x}, 0, {a.z})</a>");
                    links.AppendLine($"            <b>({b.x}, 0, {b.z})</b>");
                    links.AppendLine("          </li>");
                }
            }
            if (written.Count == 0) return;
            sb.AppendLine("      <li Class=\"Fortified.Structures.FFF_Element_ThingLinks\">");
            sb.AppendLine("        <links>");
            sb.Append(links);
            sb.AppendLine("        </links>");
            sb.AppendLine("      </li>");
        }
    }
}
