using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Fortified
{
    public class WorldObject_IncomingMissile : WorldObject_Missile
    {
        public ThingDef incomingSkyfaller;

        public override ThingDef IncomingDef => incomingSkyfaller ?? base.IncomingDef;

        protected override bool AllowGenerateTargetMap => false;

        protected override void CopyToChild(WorldObject_Missile child, bool inheritsRole)
        {
            ((WorldObject_IncomingMissile)child).incomingSkyfaller = child.mimicIncoming ?? child.config?.body?.incomingSkyfaller ?? incomingSkyfaller;
        }

        protected override void OnArrived(Map map)
        {
            if (map == null)
            {
                if (targetParent != null && targetParent.Faction == Faction.OfPlayer)
                {
                    Messages.Message("FFF.Missile.IncomingMissed".Translate(base.Faction != null ? base.Faction.Name : "?"),
                        new GlobalTargetInfo(destinationTile), MessageTypeDefOf.PositiveEvent, historical: false);
                }
                Destroy();
                return;
            }

            ThingDef def = incomingSkyfaller
                ?? (config != null && config.body != null ? config.body.incomingSkyfaller : null)
                ?? IncomingMissileUtility.DefaultIncomingDef;

            if (def != null)
            {
                IntVec3 cell = ArrivalCellFor(map, out bool loiter);
                // 從世界航跡最後一段的方向飛入地圖。
                Thing thing = MissileProjectileMaker.SpawnIncoming(def, cell, map, ArrivalHeading);
                if (thing is IIncomingMissile incoming)
                {
                    incoming.Config = config != null ? config.Clone() : null;
                    incoming.WorldPhaseDone = true;
                    incoming.Attacker = base.Faction;
                    incoming.MirvGroupId = mirvRole == MirvRole.Warhead ? mirvGroupId : -1;
                    // 命中偏移套在落點上（每枚各自落在不同格），BVR 以偏移後的落點登記
                    if (thing is Projectile_MissileIncoming p)
                    {
                        p.ApplyLandingScatter();
                        cell = p.Position;
                    }
                    // 同步交給 BVR（不依賴首-tick 時序）；盤旋飛控改註冊為盤旋波次（失敗則照常交給一般波次）。
                    bool loitered = loiter && MissileAirDefenseUtility.TryRegisterLoitering(thing, cell, incoming.Config);
                    if (!loitered) { MissileAirDefenseUtility.TryRegister(thing, cell); }
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[Fortified BVR] 來襲導彈抵達落點 {cell}（攻擊方={(base.Faction != null ? base.Faction.Name : "null")}），嘗試交給 BVR。"
                            + (loitered ? " 盤旋飛控啟動：導引頭無目標，改為盤旋等待。" : ""));
                    }
                }

                if (map.IsPlayerHome && !State.playerNotified)
                {
                    // 一小時內的後續來襲改用訊息，不再每發一封信件並暫停遊戲。
                    MissileAlertThrottle.Notify("Incoming",
                        "FFF.Missile.IncomingLetter".Translate(),
                        "FFF.Missile.IncomingLetterDesc".Translate(base.Faction != null ? base.Faction.Name : "?"),
                        LetterDefOf.ThreatBig,
                        new TargetInfo(cell, map));
                }
            }

            Destroy();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref incomingSkyfaller, "incomingSkyfaller");
        }
    }
}
