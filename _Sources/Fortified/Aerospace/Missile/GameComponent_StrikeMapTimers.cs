using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Fortified
{
    [FFF_Feature(FFF_AerospaceFeatures.Missiles)]
    public class GameComponent_StrikeMapTimers : GameComponent
    {
        public GameComponent_StrikeMapTimers(Game game)  { }
        public static GameComponent_StrikeMapTimers Comp 
        {
            get 
            {
                // 換存檔或開新遊戲時舊的實例已失效；功能關閉時遊戲沒有這個元件，回傳 null。
                if (comp == null || compGame != Current.Game)
                {
                    comp = Current.Game?.GetComponent<GameComponent_StrikeMapTimers>();
                    compGame = Current.Game;
                }
                return comp;
            }
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            times.RemoveAll(t =>
            { 
                return Find.TickManager.TicksGame >= t.Value
                ||
                t.Key == null || t.Key.Parent == null || t.Key.Parent.Destroyed;
                }
            );

        }
        public override void ExposeData()
        {
            base.ExposeData(); 
            Scribe_Collections.Look(ref this.times, "times", LookMode.Reference, LookMode.Value,
                ref this.tmp_maps, ref this.tmp_times);
            comp = null;
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                times ??= new Dictionary<Map, int>();
            }
        }

        /// <summary>這張地圖是否在打擊後的觀察期；功能關閉時一律 false。</summary>
        public static bool Observing(Map map) => map != null && Comp?.times != null && Comp.times.ContainsKey(map);


        public Dictionary<Map,int> times = new Dictionary<Map,int>();
        List<Map> tmp_maps;
        List<int> tmp_times;

        static GameComponent_StrikeMapTimers comp;
        static Game compGame;
    }
}
