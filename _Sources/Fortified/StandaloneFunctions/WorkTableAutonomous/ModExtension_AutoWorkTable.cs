using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{
    public class ModExtension_AutoWorkTable : DefModExtension
    {
        public int workTime = 300;
        public int workAmountPerStage = 60000;

        public Dictionary<SkillDef, int> skills = new Dictionary<SkillDef, int>();

        public EffecterDef phaseEffecter_east = null;
        public EffecterDef phaseEffecter_west = null;
        public EffecterDef phaseEffecter_south = null;
        public EffecterDef phaseEffecter_north = null;

        public bool northOnly = false;
        public ThingDef activeMote = null;

        // 全部階段跑完時由工作台自己結算並彈出產品，不需要殖民者再跑一趟收取。
        // 注意：仍然需要有人來準備每一個階段，這個選項只省下最後的取件動作。
        public bool autoEjectProducts = false;

        // 產品彈出的落點模式，預設沿用手動結算時的 Near。
        public ThingPlaceMode ejectPlaceMode = ThingPlaceMode.Near;

        // 彈出瞬間播放的音效，可留空。
        public SoundDef ejectSound = null;

        // 直接從「連結的儲存建築」抽取原料，備齊後由機台自行開工，全程不需要殖民者搬運。
        // 來源建築的 def 必須掛 ModExtension_MaterialSource，而且要是這台工作台的 facility 連結對象
        // （工作台的 CompProperties_AffectedByFacilities.linkableFacilities 要列出它）。
        //
        // 注意兩件事：
        //   1. 多階段配方（totalWorkAmount 大於 workAmountPerStage）仍然需要有人來準備下一個階段，
        //      這個選項只自動化「備料」與「第一次開工」。要連取件也自動化請搭配 autoEjectProducts。
        //   2. 指定了工人／奴隸／機兵限制的訂單不會被自動執行——玩家指定人選就代表他要那個人來做。
        public bool pullFromLinkedStorage = false;

        // 檢查抽料的間隔（tick）。走 IsHashIntervalTick，不同機台會自然錯開，不會擠在同一個 tick。
        // 太小只是白跑掃描，太大則是玩家看著滿倉庫發呆，120 tick（實時約 2 秒）是折衷。
        public int pullCheckIntervalTicks = 120;

        public EffecterDef doneEffecter_east = null;
        public EffecterDef doneEffecter_west = null;
        public EffecterDef doneEffecter_south = null;
        public EffecterDef doneEffecter_north = null;


        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (pullCheckIntervalTicks < 1)
            {
                yield return $"ModExtension_AutoWorkTable.pullCheckIntervalTicks 必須大於 0（目前 {pullCheckIntervalTicks}），執行時會被夾到 1。";
            }
            if (workAmountPerStage <= 0)
            {
                yield return $"ModExtension_AutoWorkTable.workAmountPerStage 必須大於 0（目前 {workAmountPerStage}），否則階段數會是無限大。";
            }
        }

        public EffecterDef GetEffecterDef_Phase(Rot4 rot)
        {
            if (rot == Rot4.East) return phaseEffecter_east;
            if (rot == Rot4.West) return phaseEffecter_west;
            if (rot == Rot4.South) return phaseEffecter_south;
            if (rot == Rot4.North) return phaseEffecter_north;
            return null;
        }
        public EffecterDef GetEffecterDef_DoneTrigger(Rot4 rot)
        {
            if (rot == Rot4.East) return doneEffecter_east;
            if (rot == Rot4.West) return doneEffecter_west;
            if (rot == Rot4.South) return doneEffecter_south;
            if (rot == Rot4.North) return doneEffecter_north;
            return null;
        }
    }
}