using RimWorld;
using Verse;
using Verse.AI;
using Multiplayer.API;

namespace Fortified
{
    /// <summary>
    /// 可為 pawn 提供額外 Gizmo 的物件（由 <see cref="Patch_Pawn_GetGizmos"/> 掃描背包與裝備欄）。
    /// </summary>
    public interface IGizmoGiver
    {
        Gizmo GetGizmoForPawn(Pawn pawn);
    }

    /// <summary>
    /// 可部署物件的共用判定。抽出成靜態工具以便 Gizmo、浮動選單與部署流程共用同一套規則。
    /// </summary>
    public static class DeployUtility
    {
        /// <summary>
        /// 該 pawn 是否有資格操作可部署物件。
        /// <see cref="IWeaponUsable"/>（機械體框架）不受智力門檻限制，其餘沿用 ToolUser 門檻。
        /// </summary>
        public static bool CanOperateDeployable(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }
            if (pawn is IWeaponUsable)
            {
                return true;
            }
            return pawn.RaceProps != null && pawn.RaceProps.intelligence >= Intelligence.ToolUser;
        }

        /// <summary>
        /// 可部署的格子：與 pawn 四方相鄰、位於地圖內、且該格沒有建築物。
        /// </summary>
        public static bool IsAcceptedDeployCell(Pawn pawn, IntVec3 cell)
        {
            if (pawn == null || pawn.Map == null)
            {
                return false;
            }
            if (!cell.IsValid || !cell.InBounds(pawn.Map))
            {
                return false;
            }
            if (!cell.AdjacentToCardinal(pawn.Position))
            {
                return false;
            }
            return cell.GetEdifice(pawn.Map) == null;
        }

        public static TargetingParameters TargetParam(Pawn pawn)
        {
            return new TargetingParameters
            {
                canTargetLocations = true,
                canTargetSelf = false,
                canTargetPawns = false,
                canTargetFires = false,
                canTargetBuildings = false,
                canTargetItems = false,
                validator = (TargetInfo x) => IsAcceptedDeployCell(pawn, x.Cell)
            };
        }

        /// <summary>
        /// 部署完成後是否要自動下令該 pawn 操作砲塔。
        /// 機械體額外受 <see cref="TurretMannableExtension"/> 白名單限制，與浮動選單使用同一套規則。
        /// </summary>
        public static bool CanAutoManTurret(Pawn pawn, Thing turret)
        {
            if (pawn == null || turret == null)
            {
                return false;
            }
            if (turret.TryGetComp<CompMannable>() == null)
            {
                return false;
            }
            if (!pawn.CanTakeOrder || pawn.jobs == null)
            {
                return false;
            }
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                return false;
            }
            if (pawn is IWeaponUsable)
            {
                return CheckUtility.IsMannable(pawn.def.GetModExtension<TurretMannableExtension>(), turret as Building_Turret);
            }
            return true;
        }
    }

    public class MinifiedThingDeployable : MinifiedThing, IGizmoGiver
    {
        MinifiedThingDeployableGraphicExt ext;

        MinifiedThingDeployableGraphicExt Ext
        {
            get
            {
                ext ??= InnerThing?.def.GetModExtension<MinifiedThingDeployableGraphicExt>();
                return ext;
            }
        }

        /// <summary>
        /// 收起狀態的外觀。<see cref="MinifiedThingDeployableGraphicExt"/> 描述的是
        /// 「這個可部署物件收起來時長什麼樣」，所以不論是拿在手上／背包裡（未 Spawned）
        /// 還是躺在地上、堆疊區裡（已 Spawned）都該套用。
        ///
        /// 先前只在 !Spawned 時採用，導致落地的迷你化物件退回 InnerThing（建築）的貼圖；
        /// 當該建築刻意把底座留白（例如只靠 turretTop 呈現的機槍陣地）時，
        /// 地面上的物件就會變成一個看不見的空箱子。
        /// </summary>
        public override Graphic Graphic
        {
            get
            {
                Graphic stowedGraphic = Ext?.graphicData?.Graphic;
                if (stowedGraphic != null)
                {
                    return stowedGraphic;
                }
                return base.Graphic;
            }
        }

        public Gizmo GetGizmoForPawn(Pawn pawn)
        {
            if (!DeployUtility.CanOperateDeployable(pawn))
            {
                return null;
            }
            Thing inner = InnerThing;
            if (inner == null)
            {
                return null;
            }

            Command_Target command_Target = new Command_Target
            {
                defaultLabel = inner.Label,
                targetingParams = DeployUtility.TargetParam(pawn),
                icon = inner.def.GetUIIconForStuff(inner.Stuff),
                action = delegate (LocalTargetInfo target)
                {
                    SyncedDeploy(this, target.Cell, pawn);
                }
            };
            if (!pawn.Drafted)
            {
                command_Target.Disable("FFF.DisabledUndrafted".Translate());
            }
            return command_Target;
        }

        /// <summary>多人模式同步入口。單機時等同直接呼叫 <see cref="Deploy"/>。</summary>
        [SyncMethod]
        public static void SyncedDeploy(MinifiedThingDeployable deployable, IntVec3 cell, Pawn workerPawn)
        {
            deployable?.Deploy(cell, workerPawn);
        }

        public bool Deploy(IntVec3 cell, Pawn workerPawn)
        {
            if (workerPawn == null || workerPawn.Destroyed)
            {
                return false;
            }
            Map map = workerPawn.Map;
            if (map == null)
            {
                return false;
            }
            if (Destroyed)
            {
                return false;
            }

            Thing createdThing = InnerThing;
            if (createdThing == null)
            {
                Log.Warning($"[FFF] MinifiedThingDeployable {this.ToStringSafe()} 沒有 InnerThing，無法部署。");
                return false;
            }

            if (!cell.IsValid || !cell.InBounds(map))
            {
                Messages.Message("FFF.MinifiedDeployable.SelectedAreaBlocked".Translate(), workerPawn, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            workerPawn.rotationTracker?.Face(cell.ToVector3Shifted());

            CellRect occupied = GenAdj.OccupiedRect(cell, workerPawn.Rotation, createdThing.def.size);
            if (!occupied.InBounds(map))
            {
                Messages.Message("FFF.MinifiedDeployable.SelectedAreaBlocked".Translate(), workerPawn, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }
            foreach (IntVec3 item in occupied)
            {
                if (item.GetEdifice(map) != null)
                {
                    Messages.Message("FFF.MinifiedDeployable.SelectedAreaBlocked".Translate(), workerPawn, MessageTypeDefOf.RejectInput, historical: false);
                    return false;
                }
            }

            GenSpawn.WipeExistingThings(cell, workerPawn.Rotation, createdThing.def, map, DestroyMode.Deconstruct);

            // 掛上執行者 context，讓 CE 之類的兼容層知道轉換過程的溢出物該交給誰。
            using (DeployContext.Push(workerPawn))
            {
                DeployCECompatHook(this, createdThing);
            }

            if (createdThing.def.CanHaveFaction)
            {
                createdThing.SetFactionDirect(workerPawn.Faction);
                createdThing.stackCount = 1;
            }

            Thing thing = GenSpawn.Spawn(createdThing, cell, map, workerPawn.Rotation, WipeMode.VanishOrMoveAside);
            // 生成失敗時 InnerThing 仍留在容器內，此時絕不能 Destroy 自己，否則砲塔會連同外殼一起消失。
            if (thing == null || !thing.Spawned)
            {
                Log.Warning($"[FFF] {createdThing.ToStringSafe()} 於 {cell} 生成失敗，取消部署。");
                return false;
            }

            if (DeployUtility.CanAutoManTurret(workerPawn, thing))
            {
                // 只在本地實際選中該 pawn 時才改動選取狀態，避免多人模式下影響其他玩家的介面。
                if (Current.ProgramState == ProgramState.Playing && Find.Selector.IsSelected(workerPawn))
                {
                    Find.Selector.Deselect(workerPawn);
                    Find.Selector.Select(thing, playSound: false, forceDesignatorDeselect: false);
                }
                Job job = JobMaker.MakeJob(RimWorld.JobDefOf.ManTurret, thing);
                workerPawn.jobs.TryTakeOrderedJob(job, 0, true);
            }

            if (!Destroyed)
            {
                Destroy();
            }
            return true;
        }

        public static void DeployCECompatHook(MinifiedThingDeployable minified, Thing turret) { }
    }

    public class MinifiedThingDeployableGraphicExt : DefModExtension
    {
        public GraphicData graphicData;
    }
}
