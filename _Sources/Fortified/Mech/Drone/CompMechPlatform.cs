using RimWorld;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using HarmonyLib;
using Multiplayer.API;

namespace Fortified
{
    public class CompMechPlatform : ThingComp, IThingHolder{

        private const int LowIngredientCountThreshold = 75;

        private int cooldownTicksRemaining;

        private ThingOwner innerContainer;

        private List<Pawn> spawnedPawns = new List<Pawn>();

        public int maxToFill;

        private int selectedAreaId = -1;

        // 玩家在「部署型號」選單裡選定的無人機類型。null = 沿用清單裡第一個可用的型號。
        // 只存 Def 參照：型號日後被移除、或所需研究被改動時，
        // ActiveOption 會自動退回其他可用選項，不會讓整台平台卡死。
        private PawnKindDef selectedKind;

        // Props.spawnPawnKinds 正規化後的結果（濾掉 null／重複，並補上舊版的 spawnPawnKind）。
        // Props 由同一個 Def 的所有實例共用，這裡只讀不改；per-instance 狀態一律放在 comp 自己身上。
        private List<MechPlatformSpawnOption> cachedOptions;

        // Area.ID 只在「同一張地圖的同一個 Area 物件」上有意義。重力船起飛時原版是用
        // MoveableArea_Allowed.TryCreateArea 在新地圖上「依名稱」重建區域，新區域拿到的是全新的 ID，
        // 因此只存 ID 會在換圖後查無此區域，限制被靜默還原成「無限制」。
        // 額外記住名稱，換圖後照原版的規則重新綁定。
        private string selectedAreaLabel;

        // 上一次實際把限制推給無人機時所在的地圖，僅供偵測換圖用，不需存檔。
        private Map lastAppliedMap;

        public Area SelectedArea
        {
            get
            {
                Map map = parent?.Map;
                if (map == null)
                {
                    return null;
                }
                if (selectedAreaId < 0 && selectedAreaLabel.NullOrEmpty())
                {
                    return null;
                }

                Area area = null;
                if (selectedAreaId >= 0)
                {
                    area = map.areaManager.AllAreas.FirstOrDefault(a => a != null && a.ID == selectedAreaId);
                }
                if (area == null && !selectedAreaLabel.NullOrEmpty())
                {
                    // 換圖後依名稱重新對上新地圖的同名區域（和原版 MoveableArea_Allowed 的做法一致）。
                    area = map.areaManager.GetLabeled(selectedAreaLabel);
                    if (area != null && !area.AssignableAsAllowed())
                    {
                        area = null;
                    }
                }
                return area;
            }
        }

        public void SetSelectedArea(Area area)
        {
            selectedAreaId = area?.ID ?? -1;
            selectedAreaLabel = area?.Label;
            PushAreaToPawns(applyUnrestricted: true);
        }

        /// <summary>
        /// 把目前選定的活動區同步給所有已部署的無人機。
        /// applyUnrestricted 為 false 時，只在真的有選定區域時才動手，
        /// 避免換圖／讀檔時把玩家手動設定的個別限制清掉。
        /// </summary>
        private void PushAreaToPawns(bool applyUnrestricted)
        {
            Map map = parent?.Map;
            if (map == null || spawnedPawns.NullOrEmpty())
            {
                return;
            }
            Area area = SelectedArea;
            if (area == null && !applyUnrestricted)
            {
                return;
            }
            // 換圖後重新綁定成功時，把 ID 更新成新地圖上的那一個，之後就不必每次都查名稱。
            if (area != null)
            {
                selectedAreaId = area.ID;
            }
            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                Pawn p = spawnedPawns[i];
                if (p == null || p.Dead || p.playerSettings == null || p.MapHeld != map)
                {
                    continue;
                }
                p.playerSettings.AreaRestrictionInPawnCurrentMap = area;
            }
        }

        /// <summary>
        /// 目前還活著、且仍在同一張地圖上的已部署單位數量。
        /// 提供給 AI 節點判斷「還需不需要再放一批」用。
        /// </summary>
        public int LiveSpawnedPawnCount
        {
            get
            {
                if (spawnedPawns.NullOrEmpty())
                {
                    return 0;
                }
                Map map = parent?.MapHeld;
                int count = 0;
                for (int i = 0; i < spawnedPawns.Count; i++)
                {
                    Pawn p = spawnedPawns[i];
                    if (p != null && !p.Dead && p.Spawned && (map == null || p.Map == map))
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public CompProperties_MechPlatform Props => (CompProperties_MechPlatform)props;

        #region 部署型號（spawnPawnKinds）

        /// <summary>這台平台能部署的所有型號，已濾掉無效條目。永遠不為 null，但可能是空清單。</summary>
        public List<MechPlatformSpawnOption> SpawnOptions
        {
            get
            {
                if (cachedOptions != null)
                {
                    return cachedOptions;
                }
                List<MechPlatformSpawnOption> list = new List<MechPlatformSpawnOption>();
                CompProperties_MechPlatform p = Props;
                if (p != null)
                {
                    List<MechPlatformSpawnOption> declared = p.spawnPawnKinds;
                    if (declared != null)
                    {
                        for (int i = 0; i < declared.Count; i++)
                        {
                            MechPlatformSpawnOption o = declared[i];
                            // pawnKind 解析失敗（打錯字、缺前置 mod）時這裡會是 null。
                            // 單筆壞資料只跳過自己，不能把整台平台的部署功能一起拖垮。
                            if (o?.pawnKind == null || ContainsKind(list, o.pawnKind))
                            {
                                continue;
                            }
                            list.Add(o);
                        }
                    }
                    // 舊版的單一型號欄位仍然有效：沒寫 spawnPawnKinds 時它就是唯一選項，
                    // 兩個都寫時它排在最前面，等於預設型號。
                    if (p.spawnPawnKind != null && !ContainsKind(list, p.spawnPawnKind))
                    {
                        list.Insert(0, p.LegacyOption);
                    }
                }
                cachedOptions = list;
                return cachedOptions;
            }
        }

        private static bool ContainsKind(List<MechPlatformSpawnOption> list, PawnKindDef kind)
        {
            if (list == null || kind == null)
            {
                return false;
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i]?.pawnKind == kind)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 型號是否已解鎖。研究限制只對玩家陣營生效；
        /// 還沒進遊戲（Def 載入階段、主選單）時一律放行 —— 這條路徑寧可 fail-open，
        /// 也不要在沒有 Game 實例時丟例外。
        /// </summary>
        public bool IsOptionUnlocked(MechPlatformSpawnOption option)
        {
            if (option?.pawnKind == null)
            {
                return false;
            }
            if (option.requiredResearch == null)
            {
                return true;
            }
            if (parent?.Faction != Faction.OfPlayer)
            {
                return true;
            }
            if (Current.ProgramState != ProgramState.Playing || Find.ResearchManager == null)
            {
                return true;
            }
            try
            {
                return option.requiredResearch.IsFinished;
            }
            catch (Exception e)
            {
                Log.WarningOnce($"[Fortified] CompMechPlatform failed to read research state for {option.requiredResearch.defName}: {e.Message}", option.requiredResearch.shortHash);
                return true;
            }
        }

        /// <summary>
        /// 目前實際會部署的型號設定。找不到任何可用型號時回傳 null，
        /// 呼叫端一律要能處理 null（CanSpawn 會先擋下來）。
        /// </summary>
        public MechPlatformSpawnOption ActiveOption
        {
            get
            {
                List<MechPlatformSpawnOption> opts = SpawnOptions;
                if (opts.Count == 0)
                {
                    return null;
                }
                if (selectedKind != null)
                {
                    for (int i = 0; i < opts.Count; i++)
                    {
                        if (opts[i].pawnKind == selectedKind && IsOptionUnlocked(opts[i]))
                        {
                            return opts[i];
                        }
                    }
                }
                // 選定的型號被移除或還沒解鎖時退回第一個可用的。
                // 這裡刻意不改寫 selectedKind：getter 不做狀態變更，
                // 免得多人連線兩端因為讀取時機不同而各自寫入不同的值。
                for (int i = 0; i < opts.Count; i++)
                {
                    if (IsOptionUnlocked(opts[i]))
                    {
                        return opts[i];
                    }
                }
                return null;
            }
        }

        public PawnKindDef ActiveKind => ActiveOption?.pawnKind;

        public PawnKindDef SelectedKind => selectedKind;

        /// <summary>
        /// 切換部署型號。只接受清單裡真的存在的型號（null = 回到預設），
        /// 避免舊存檔或外部呼叫塞進不合法的值。
        /// </summary>
        public void SetSelectedKind(PawnKindDef kind)
        {
            if (kind != null && !ContainsKind(SpawnOptions, kind))
            {
                return;
            }
            selectedKind = kind;
        }

        /// <summary>單台的材料成本。型號沒指定時沿用 Props，並保底為 1 以免除以零。</summary>
        public int CostOf(MechPlatformSpawnOption option)
        {
            int cost = (option != null && option.costPerPawn > 0) ? option.costPerPawn : (Props?.costPerPawn ?? 1);
            return Mathf.Max(1, cost);
        }

        public int MaxPawnsOf(MechPlatformSpawnOption option)
        {
            int max = (option != null && option.maxPawnsToSpawn > 0) ? option.maxPawnsToSpawn : (Props?.maxPawnsToSpawn ?? 0);
            return Mathf.Max(0, max);
        }

        public int CooldownOf(MechPlatformSpawnOption option)
        {
            int cd = (option != null && option.cooldownTicks >= 0) ? option.cooldownTicks : (Props?.cooldownTicks ?? 0);
            return Mathf.Max(0, cd);
        }

        /// <summary>指定型號現在最多能放幾台。型號無效時回 0；成本已在 CostOf 保底為 1。</summary>
        public int MaxCanSpawnOf(MechPlatformSpawnOption option)
        {
            if (option?.pawnKind == null)
            {
                return 0;
            }
            return Mathf.Min(IngredientCount / CostOf(option), MaxPawnsOf(option));
        }

        /// <summary>這台平台目前吃的成本／上限／冷卻（＝目前選定型號的值）。</summary>
        public int CostPerPawn => CostOf(ActiveOption);

        public int MaxPawnsPerDeploy => MaxPawnsOf(ActiveOption);

        public int CooldownTicks => CooldownOf(ActiveOption);

        /// <summary>依 PawnKindDef 反查型號設定；查不到回 null（呼叫端會退回 Props 的預設值）。</summary>
        public MechPlatformSpawnOption OptionForKind(PawnKindDef kind)
        {
            if (kind == null)
            {
                return null;
            }
            List<MechPlatformSpawnOption> opts = SpawnOptions;
            for (int i = 0; i < opts.Count; i++)
            {
                if (opts[i].pawnKind == kind)
                {
                    return opts[i];
                }
            }
            return null;
        }

        private string ActiveLabel => ActiveOption?.Label ?? Props?.spawnPawnKind?.label ?? string.Empty;

        private string ActiveLabelPlural => ActiveOption?.LabelPlural ?? Props?.spawnPawnKind?.labelPlural ?? ActiveLabel;

        /// <summary>
        /// 收回鈕的文案。清單裡有多種型號時場上可能混編，這時改用通用稱呼，
        /// 免得寫出「收回所有獵犬」卻連垃圾桶一起收走的誤導。
        /// </summary>
        private string RetractLabelPlural => SpawnOptions.Count > 1 ? "FFF.Drone.GenericPlural".Translate().ToString() : ActiveLabelPlural;

        /// <summary>找不到貼圖時退回 Props 的路徑，再不行就用 BadTex，不會像原本那樣直接噴紅字。</summary>
        private static Texture2D SafeIcon(string path, string fallbackPath)
        {
            Texture2D tex = null;
            if (!path.NullOrEmpty())
            {
                tex = ContentFinder<Texture2D>.Get(path, reportFailure: false);
            }
            if (tex == null && !fallbackPath.NullOrEmpty() && fallbackPath != path)
            {
                tex = ContentFinder<Texture2D>.Get(fallbackPath, reportFailure: false);
            }
            return tex ?? BaseContent.BadTex;
        }

        #endregion

        public virtual AcceptanceReport CanSpawn
        {
            get
            {
                if (parent is Pawn pawn)
                {
                    if (pawn.IsSelfShutdown())
                    {
                        return "SelfShutdown".Translate();
                    }

                    if (pawn.Faction == Faction.OfPlayer && !pawn.IsColonyMechPlayerControlled)
                    {
                        return false;
                    }

                    if (!pawn.Awake() || pawn.Downed || pawn.Dead || !pawn.Spawned)
                    {
                        return false;
                    }
                }
                else if (parent is Building building)
                {
                    if (building.TryGetComp<CompPowerTrader>(out var _power)&& !_power.PowerOn )
                    {
                        return "NoPower".Translate();
                    }
                    if (building.TryGetComp<CompBreakdownable>(out var _broke)&& _broke.BrokenDown)
                    {
                        return "BrokenDown".Translate();
                    }
                    if (building.TryGetComp<CompFlickable>(out var _flick)&& !_flick.SwitchIsOn)
                    {
                        return "Deactivated".Translate();
                    }
                }

                if (ActiveOption == null)
                {
                    // spawnPawnKind / spawnPawnKinds 都沒給，或清單裡的型號全部還沒解鎖。
                    return "FFF.Drone.NoSpawnKind".Translate();
                }

                if (MaxCanSpawn <= 0)
                {
                    return "MechCarrierNotEnoughResources".Translate();
                }

                if (cooldownTicksRemaining > 0)
                {
                    return "CooldownTime".Translate() + " " + cooldownTicksRemaining.ToStringSecondsFromTicks();
                }

                return true;
            }
        }

        public virtual int IngredientCount
        {
            get
            {
                // innerContainer 在「comp 被後加進既有存檔」或重力船搬運途中可能還沒建立；
                // 這個屬性被 WorkGiver、AI 節點、UI 高頻呼叫，不能假設它一定存在。
                if (innerContainer == null || Props?.fixedIngredient == null)
                {
                    return 0;
                }
                return innerContainer.TotalStackCountOfDef(Props.fixedIngredient);
            }
        }

        public virtual int AmountToAutofill => Mathf.Max(0, maxToFill - IngredientCount);

        public virtual int MaxCanSpawn => MaxCanSpawnOf(ActiveOption);

        public bool LowIngredientCount => IngredientCount < LowIngredientCountThreshold;

        public float PercentageFull
        {
            get
            {
                // maxIngredientCount 沒填就是 0，直接除會得到 Infinity／NaN，
                // 進度條之類的呼叫端會畫出爛東西。
                int max = Props?.maxIngredientCount ?? 0;
                return max <= 0 ? 0f : Mathf.Clamp01((float)IngredientCount / (float)max);
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            if (!ModLister.CheckBiotech("Mech carrier"))
            {
                parent.Destroy();
                return;
            }

            base.PostSpawnSetup(respawningAfterLoad);
            CleanupSpawnedPawns();

            // 讀檔或換圖（重力船降落）之後重新把限制推一次，SelectedArea 會順便完成依名稱的重新綁定。
            // 無人機可能比平台晚落地，交給 CompTickInterval 再補推幾次。
            lastAppliedMap = parent?.Map;
            areaPushesRemaining = 4;
            PushAreaToPawns(applyUnrestricted: false);

            if (!respawningAfterLoad && !parent.BeingTransportedOnGravship)
            {
                var c = Props.startingIngredientCount;

                // parent.Faction 可能是 null（DevMode 直接放的建築、野生機兵）。
                // null 的情況走保守路線：當成玩家方，不自動填滿也不開自動部署。
                if (parent.Faction != null && !parent.Faction.IsPlayer)
                {
                    // NPC 開場自動填滿；是否要自己按節奏投放則由 npcAutoDeploy 決定，
                    // 關掉之後投放時機交給思考樹上的 JobGiver 控制。
                    this.autoDeployEnabled = Props.npcAutoDeploy;
                    c = Props.maxIngredientCount;
                }

                innerContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
                if (c > 0 && Props.fixedIngredient != null)
                {
                    // stackLimit 為 0 時 Mathf.Min(count, 0) 也是 0，count 永遠不會減少 —— 直接卡死遊戲。
                    int stackLimit = Mathf.Max(1, Props.fixedIngredient.stackLimit);
                    int count = c;
                    while (count > 0)
                    {
                        int batch = Mathf.Min(count, stackLimit);
                        Thing thing = ThingMaker.MakeThing(Props.fixedIngredient);
                        thing.stackCount = batch;
                        innerContainer.TryAdd(thing, batch);
                        count -= batch;
                    }
                }
                maxToFill = c;
            }

            // 讀檔／重力船搬運路徑不會走到上面的建立分支；容器仍必須存在，
            // 否則之後每一次 Retracted、ReleaseOverFilled 都會炸。
            innerContainer ??= new ThingOwner<Thing>(this, oneStackOnly: false);
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            if (AmountToAutofill <= 0 || Props?.fixedIngredient == null) yield break;
            FloatMenuOption floatMenuOption = new FloatMenuOption("FFF.FillMechPlatform".Translate(Props.fixedIngredient.label, AmountToAutofill), delegate
            {
                List<Thing> list = HaulAIUtility.FindFixedIngredientCount(selPawn, this.Props.fixedIngredient, AmountToAutofill);
                if (!list.NullOrEmpty())
                {
                    Job job = HaulAIUtility.HaulToContainerJob(selPawn, list[0], this.parent);
                    job.count = Mathf.Min(job.count, AmountToAutofill);
                    job.targetQueueB = (from i in list.Skip(1) select new LocalTargetInfo(i)).ToList();
                    selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc, false);
                }
            }, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0);
            if (!floatMenuOption.Disabled)
            {
                yield return floatMenuOption;
            }
        }

        public void TrySpawnPawns()
        {
            TrySpawnPawns(PickOptionToDeploy());
        }

        /// <summary>
        /// 決定這一批要放哪個型號。玩家陣營照選單走；
        /// NPC 在 npcRandomKind 開啟時每批隨機抽一種（權重來自 selectionWeight），
        /// 讓同一台載具的護航編隊不會永遠只有一種機型。
        /// </summary>
        protected virtual MechPlatformSpawnOption PickOptionToDeploy()
        {
            if (parent?.Faction != Faction.OfPlayer && (Props?.npcRandomKind ?? false))
            {
                List<MechPlatformSpawnOption> opts = SpawnOptions;
                if (opts.Count > 1)
                {
                    // 只從「現在真的放得出來」的型號裡抽，不然抽到最貴的那種會整批落空。
                    List<MechPlatformSpawnOption> candidates = opts.Where(o => IsOptionUnlocked(o) && MaxCanSpawnOf(o) > 0).ToList();
                    if (candidates.Count > 0
                        && candidates.TryRandomElementByWeight(o => Mathf.Max(0.0001f, o.selectionWeight), out MechPlatformSpawnOption picked))
                    {
                        return picked;
                    }
                }
            }
            return ActiveOption;
        }

        /// <summary>
        /// 依指定型號投放一批無人機。option 為 null 時直接放棄，不會偷偷退回 Props.spawnPawnKind，
        /// 免得出現「選了 A 卻放出 B」。
        /// </summary>
        public virtual void TrySpawnPawns(MechPlatformSpawnOption option)
        {
            if (option?.pawnKind == null || parent == null || !parent.Spawned || parent.Map == null)
            {
                return;
            }
            int maxCanSpawn = MaxCanSpawnOf(option);
            if (maxCanSpawn <= 0)
            {
                return;
            }
            int costPerPawn = CostOf(option);

            PawnGenerationRequest request = new PawnGenerationRequest(option.pawnKind, parent.Faction, PawnGenerationContext.NonPlayer, null, forceGenerateNewPawn: true, allowDead: false, allowDowned: false, canGeneratePawnRelations: true, mustBeCapableOfViolence: false, 1f, forceAddFreeWarmLayerIfNeeded: false, allowGay: true, allowPregnant: false, allowFood: true, allowAddictions: true, inhabitant: false, certainlyBeenInCryptosleep: false, forceRedressWorldPawnIfFormerColonist: false, worldPawnFactionDoesntMatter: false, 0f, 0f, null, 1f, null, null, null, null, null, null, null, null, null, null, null, null, forceNoIdeo: false, forceNoBackstory: false, forbidAnyTitle: false, forceDead: false, null, null, null, null, null, 0f, DevelopmentalStage.Newborn);
            Lord lord = ((parent is Pawn p) ? p.GetLord() : null);
            int spawnedThisBatch = 0;

            for (int i = 0; i < maxCanSpawn; i++)
            {
                // 每一台都重新確認材料：別的來源（玩家手動倒空、DEV 指令）可能在同一批中途掏空容器。
                if (IngredientCount < costPerPawn)
                {
                    break;
                }

                Pawn pawn;
                try
                {
                    pawn = PawnGenerator.GeneratePawn(request);
                }
                catch (Exception e)
                {
                    // 型號本身有問題（缺 race、缺裝備）時整批收手，但不能讓例外冒到 gizmo 的 action 外面。
                    Log.Error($"[Fortified] CompMechPlatform on {parent.ToStringSafe()} failed to generate {option.pawnKind.defName}: {e}");
                    break;
                }
                if (pawn == null)
                {
                    break;
                }

                // Set the pawn's platform if it is a drone.
                if (pawn.TryGetComp<CompDrone>(out var d))
                {
                    d.SetPlatform(parent);
                }

                GenSpawn.Spawn(pawn, parent.Position, parent.Map);
                if (!pawn.Spawned)
                {
                    // 落地失敗（位置無效）就地銷毀，材料也不扣，避免「錢花了機沒出來」。
                    if (!pawn.Destroyed)
                    {
                        pawn.Destroy();
                    }
                    break;
                }

                spawnedPawns.Add(pawn);
                lord?.AddPawn(pawn);

                Area selectedArea = SelectedArea;
                if (selectedArea != null && pawn.playerSettings != null)
                {
                    pawn.playerSettings.AreaRestrictionInPawnCurrentMap = selectedArea;
                }

                ConsumeIngredient(costPerPawn);
                spawnedThisBatch++;

                if (Props.spawnedMechEffecter != null)
                {
                    Effecter effecter = new Effecter(Props.spawnedMechEffecter);
                    effecter.Trigger(Props.attachSpawnedMechEffecter ? ((TargetInfo)pawn) : new TargetInfo(pawn.Position, pawn.Map), TargetInfo.Invalid);
                    effecter.Cleanup();
                }
            }

            // 一台都沒放出來就不進冷卻，否則一次失敗會白白鎖住整段時間。
            if (spawnedThisBatch <= 0)
            {
                return;
            }

            cooldownTicksRemaining = CooldownOf(option);
            if (Props.spawnEffecter != null)
            {
                Effecter effecter2 = new Effecter(Props.spawnEffecter);
                effecter2.Trigger(Props.attachSpawnedEffecter ? ((TargetInfo)parent) : new TargetInfo(parent.Position, parent.Map), TargetInfo.Invalid);
                effecter2.Cleanup();
            }
        }

        /// <summary>
        /// 從內部容器扣掉指定數量的材料，並回傳實際扣掉的量。
        /// 原版 CompMechCarrier 的扣料迴圈是照「每台的成本」去拿，而不是照「還差多少」去拿：
        /// 當某一疊剩下的量不足一台份時，它會先把那疊拿光，接著又從下一疊再整整拿一份成本，
        /// 一次投放多台就會超扣（獵犬平台每台 50 鋼，投放兩台可能吃掉 125）；
        /// 而回收時只退回 costPerPawn，於是部署與回收的數量就對不起來。
        /// 另外那份 tmpResources 快照裡已被拿光銷毀的堆疊，下一輪還會再被 Take 一次，
        /// 這時 ThingOwner.Take 會記一筆錯誤並回傳 null，扣料中途直接中斷。
        /// 這裡改成直接照容器現況、照缺口扣，並且只認 fixedIngredient。
        /// </summary>
        protected int ConsumeIngredient(int amount)
        {
            if (amount <= 0 || innerContainer == null || Props.fixedIngredient == null)
            {
                return 0;
            }
            int consumed = 0;
            for (int i = innerContainer.Count - 1; i >= 0 && amount > 0; i--)
            {
                Thing thing = innerContainer[i];
                if (thing == null || thing.def != Props.fixedIngredient || thing.stackCount <= 0)
                {
                    continue;
                }
                Thing taken = innerContainer.Take(thing, Mathf.Min(thing.stackCount, amount));
                if (taken == null)
                {
                    continue;
                }
                amount -= taken.stackCount;
                consumed += taken.stackCount;
                taken.Destroy();
            }
            return consumed;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent is Building b && this.parent.Faction != Faction.OfPlayer)
            {
                yield break;
            }
            if (parent is Pawn pawn && !pawn.IsColonyMech && pawn.GetOverseer() == null)
            {
                yield break;
            }
            foreach (Gizmo item in base.CompGetGizmosExtra())
            {
                yield return item;
            }

            // Add the gizmo to retract spawned pawns if there are any.
            if (!spawnedPawns.NullOrEmpty())
            {
                Command_Action command_Action = new Command_Action
                {
                    action = delegate
                    {
#if MULTIPLAYER
                        [SyncMethod] void SyncRetract() {
                            foreach (Pawn item in spawnedPawns)
                            {
                                if (item.TryGetComp<CompDrone>(out var d))
                                {
                                    d.ReturnToPlatform();
                                }
                            }
                        }
                        SyncRetract();
#else
                        foreach (Pawn item in spawnedPawns)
                        {
                            if (item.TryGetComp<CompDrone>(out var d))
                            {
                                d.ReturnToPlatform();
                            }
                        }
#endif
                    },

                    hotKey = KeyBindingDefOf.Misc3,
                    icon = SafeIcon(Props.gizmoIconPath_Retract, null),
                    // 只寫 spawnPawnKinds、沒寫 spawnPawnKind 的定義會讓原本的 Props.spawnPawnKind.labelPlural 直接 NRE。
                    defaultLabel = "FFF.RetractDrones".Translate(RetractLabelPlural),
                    defaultDesc = "FFF.RetractDronesDesc".Translate(RetractLabelPlural, Props.fixedIngredient?.label ?? string.Empty)
                };
                yield return command_Action;
            }

            AcceptanceReport canSpawn = CanSpawn;
            MechPlatformSpawnOption activeOption = ActiveOption;
            Command_ActionWithCooldown act = new Command_ActionWithCooldown
            {
                cooldownPercentGetter = () => Mathf.InverseLerp(CooldownTicks, 0f, cooldownTicksRemaining),
                action = delegate
                {
#if MULTIPLAYER
                    [SyncMethod] void SyncedTrySpawnPawns() { TrySpawnPawns(); }
                    SyncedTrySpawnPawns();
#else
                    TrySpawnPawns();
#endif
                },
                hotKey = KeyBindingDefOf.Misc2,
                Disabled = !canSpawn.Accepted,
                // 圖示、文案、數量全部改讀「目前選定的型號」。
                icon = SafeIcon(activeOption?.gizmoIconPath, Props.gizmoIconPath),
                defaultLabel = "FFF.DeployDrone".Translate(ActiveLabelPlural),
                defaultDesc = "FFF.DeployDroneDesc".Translate(Props.fixedIngredient?.label ?? string.Empty, MaxPawnsPerDeploy, ActiveLabel, ActiveLabelPlural, ActiveLabel)
            };
            if (!canSpawn.Reason.NullOrEmpty())
            {
                act.Disable(canSpawn.Reason);
            }
            Command_Toggle command_Toggle = new Command_Toggle
            {
                defaultLabel = "FFF.AutoDeploy".Translate(),
                defaultDesc = "FFF.AutoDeployDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Drone_AutoDeploy"),
                isActive = () => autoDeployEnabled,
                toggleAction = () =>
                {
#if MULTIPLAYER
                    [SyncMethod] void SyncToggle() { autoDeployEnabled = !autoDeployEnabled; }
                    SyncToggle();
#else
                    autoDeployEnabled = !autoDeployEnabled;
#endif
                }
            };
            yield return command_Toggle;


            if (DebugSettings.ShowDevGizmos)
            {
                if (cooldownTicksRemaining > 0)
                {
                    Command_Action command_Action = new Command_Action();
                    command_Action.defaultLabel = "DEV: Reset cooldown";
                    command_Action.action = delegate
                    {
                        cooldownTicksRemaining = 0;
                    };
                    yield return command_Action;
                }

                string ingredientLabelDev = Props?.fixedIngredient?.label ?? "ingredient";

                Command_Action command_Action2 = new Command_Action();
                command_Action2.defaultLabel = "DEV: Fill with " + ingredientLabelDev;
                command_Action2.action = delegate
                {
#if MULTIPLAYER
                    [SyncMethod] void SyncFill() { DevFill(); }
                    SyncFill();
#else
                    DevFill();
#endif
                };

                yield return command_Action2;
                Command_Action command_Action3 = new Command_Action();
                command_Action3.defaultLabel = "DEV: Empty " + ingredientLabelDev;
                command_Action3.action = delegate
                {
#if MULTIPLAYER
                    [SyncMethod] void SyncDevEmpty() { innerContainer?.ClearAndDestroyContents(); }
                    SyncDevEmpty();
#else
                    innerContainer?.ClearAndDestroyContents();
#endif
                };
                yield return command_Action3;
            }

            yield return act;


            // 只有真的有得選時才佔一格 gizmo，單一型號的舊平台介面完全不變。
            if (SpawnOptions.Count > 1)
            {
                yield return new Command_Action
                {
                    defaultLabel = "FFF.Drone.SelectKind".Translate(ActiveLabel.CapitalizeFirst()),
                    defaultDesc = "FFF.Drone.SelectKindDesc".Translate(),
                    icon = SafeIcon(activeOption?.gizmoIconPath, Props.gizmoIconPath),
                    action = () =>
                    {
                        List<FloatMenuOption> kindOptions = SpawnKindOptions();
                        if (kindOptions.NullOrEmpty())
                        {
                            return;
                        }
                        Find.WindowStack.Add(new FloatMenu(kindOptions));
                    }
                };
            }

            TaggedString currentLabel = SelectedArea != null ? SelectedArea.Label : "FFF.Drone.NoRestrict".Translate();
            yield return new Command_Action
            {
                defaultLabel = currentLabel,
                defaultDesc = "FFF.Drone.AllowedAreaDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Drone_AreaAllowed", true),
                defaultIconColor = SelectedArea?.Color ?? Color.white,
                action = () =>
                {
                    var options = AreaOptions(parent.Map);
                    if (options.NullOrEmpty()) return;
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            };
        }

        /// <summary>
        /// 型號選單。未解鎖的型號留在清單裡但點不動，讓玩家知道還有東西可以解鎖。
        /// </summary>
        private List<FloatMenuOption> SpawnKindOptions()
        {
            List<FloatMenuOption> list = new List<FloatMenuOption>();
            List<MechPlatformSpawnOption> opts = SpawnOptions;
            string ingredientLabel = Props?.fixedIngredient?.label ?? string.Empty;
            for (int i = 0; i < opts.Count; i++)
            {
                MechPlatformSpawnOption option = opts[i];
                if (option?.pawnKind == null)
                {
                    continue;
                }
                string label = $"{option.Label.CapitalizeFirst()} ({CostOf(option)} {ingredientLabel})";
                if (!IsOptionUnlocked(option))
                {
                    string research = option.requiredResearch?.LabelCap ?? string.Empty;
                    // action 傳 null 就是不可點的灰項。
                    list.Add(new FloatMenuOption($"{label} — {"FFF.Drone.KindLocked".Translate(research)}", null));
                    continue;
                }
                PawnKindDef kind = option.pawnKind;
                list.Add(new FloatMenuOption(label, () =>
                {
#if MULTIPLAYER
                    [SyncMethod] void SyncSelectedKind(PawnKindDef k, CompMechPlatform self) { self.SetSelectedKind(k); }
                    SyncSelectedKind(kind, this);
#else
                    SetSelectedKind(kind);
#endif
                }));
            }
            return list;
        }

        private List<FloatMenuOption> AreaOptions(Map map)
        {
            var list = new List<FloatMenuOption>
            {
                new FloatMenuOption("FFF.Drone.NoRestrict".Translate(), () =>
                {
#if MULTIPLAYER
                    [SyncMethod] void SyncUnrestricted(CompMechPlatform self) { self.SetSelectedArea(null); }
                    SyncUnrestricted(this);
#else
                    SetSelectedArea(null);
#endif
                })
            };
            foreach (var area in map.areaManager.AllAreas.Where(a=>a.AssignableAsAllowed()))
            {
                var label = area.Label;
                var opt = new FloatMenuOption(label, () =>
                {
#if MULTIPLAYER
                    [SyncMethod] void SyncSelectedArea(Area area, CompMechPlatform self) { self.SetSelectedArea(area); }
                    SyncSelectedArea(area, this);
#else
                    SetSelectedArea(area);
#endif
                });
                // �B�~�b�k���e�X�C��w���p���
                opt.extraPartWidth = 24f;
                opt.extraPartOnGUI = rect =>
                {
                    var colorRect = new Rect(rect.xMax - 20f, rect.y + (rect.height - 14f) / 2f, 14f, 14f);
                    Widgets.DrawBoxSolidWithOutline(colorRect, area.Color, Color.black, 1);
                    return false;
                };

                list.Add(opt);
            }
            return list;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }

        public override string CompInspectStringExtra()
        {
            string text = base.CompInspectStringExtra();
            if (text.NullOrEmpty()) text = "";

            text += "CasketContains".Translate() + ": " + (innerContainer?.ContentsString?.CapitalizeFirst() ?? string.Empty);
            if (SpawnOptions.Count > 1)
            {
                text += "\n" + "FFF.Drone.SelectKind".Translate(ActiveLabel.CapitalizeFirst());
            }
            if (autoDeployEnabled)
            {
                text += "\n" + "FFF.AutoDeployEnabled".Translate();
            }
            return text;
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            innerContainer?.ClearAndDestroyContents();
            if (spawnedPawns == null)
            {
                return;
            }
            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                // 清單裡可能有 null（存檔參照解不到）或已被銷毀的個體，
                // 對它們呼叫 Kill 會直接丟例外，而 PostDestroy 是不該失敗的路徑。
                Pawn p = spawnedPawns[i];
                if (p != null && !p.Dead && !p.Destroyed)
                {
                    p.Kill(null, null);
                }
            }
        }

        public void Retracted(Pawn pawn)
        {
            spawnedPawns.Remove(pawn);
            // 退款一律照「這台無人機自己的型號」算，不是照選單上當下選的那個。
            // 否則玩家可以放便宜的、把選單切成貴的、再收回來無限刷材料。
            // 查不到型號（例如該型號已從 Def 移除）時退回 Props 的預設成本。
            RefundIngredient(CostOf(OptionForKind(pawn?.kindDef)));
        }

        /// <summary>
        /// 把一台的材料退回平台。退回的量固定等於投放成本，兩邊才會對得起來。
        /// 舊版直接做 Mathf.Min(costPerPawn, stackLimit)，成本大於堆疊上限時會少退；
        /// 而且只要容器差一點就裝不下，整份退款都會掉到地上，這裡改成裝得下多少放多少，
        /// 只有真的溢出的部分才落地。
        /// </summary>
        protected void RefundIngredient(int amount)
        {
            if (amount <= 0 || Props.fixedIngredient == null)
            {
                return;
            }
            int stackLimit = Mathf.Max(1, Props.fixedIngredient.stackLimit);
            while (amount > 0)
            {
                int count = Mathf.Min(amount, stackLimit);
                amount -= count;

                int spaceLeft = (innerContainer == null) ? 0 : Mathf.Max(0, Props.maxIngredientCount - IngredientCount);
                int toContainer = Mathf.Min(count, spaceLeft);
                if (toContainer > 0)
                {
                    Thing thing = ThingMaker.MakeThing(Props.fixedIngredient);
                    thing.stackCount = toContainer;
                    if (innerContainer == null || !innerContainer.TryAdd(thing))
                    {
                        DropIngredient(thing);
                    }
                }

                int toGround = count - toContainer;
                if (toGround > 0)
                {
                    Thing thing2 = ThingMaker.MakeThing(Props.fixedIngredient);
                    thing2.stackCount = toGround;
                    DropIngredient(thing2);
                }
            }
        }

        private void DropIngredient(Thing thing)
        {
            Map map = parent?.MapHeld;
            if (map == null || !GenPlace.TryPlaceThing(thing, parent.PositionHeld, map, ThingPlaceMode.Near))
            {
                if (!thing.Destroyed)
                {
                    thing.Destroy();
                }
            }
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            if (!Find.Selector.IsSelected(parent))
            {
                return;
            }


            if (spawnedPawns == null)
            {
                return;
            }
            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                Pawn p = spawnedPawns[i];
                // 同時要求非 null 與同一張地圖：跨圖的無人機座標在這張圖上沒有意義，
                // 連出去的線會橫貫整張地圖。
                if (p != null && !p.Dead && p.Spawned && p.Map == parent.Map)
                {
                    GenDraw.DrawLineBetween(parent.TrueCenter(), p.TrueCenter());
                }
            }
        }
        protected void CleanupSpawnedPawns()
        {
            // �M�z���Ī��ͦ����
            List<Pawn> pawns = spawnedPawns;
            if (pawns == null || pawns.Count <= 0) return;
            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                var pawn = pawns[i];
                // 也要清掉「已銷毀但沒死」的個體：回收、DevMode 刪除、其他 mod 移除都會留下這種殘骸。
                // 留著的話 CompTickInterval 的 spawnedPawns.Count 門檻會被永久墊高，自動部署從此不再觸發。
                if (pawn == null || pawn.Dead || pawn.Destroyed)
                {
                    pawns.RemoveAt(i);
                }
            }
            spawnedPawns = pawns;
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_Values.Look(ref cooldownTicksRemaining, "cooldownTicksRemaining", 0);
            Scribe_Values.Look(ref autoDeployTicks, "autoDeployTicks", 0);
            Scribe_Values.Look(ref autoDeployEnabled, "autoDeployEnabled", false);
            Scribe_Values.Look(ref maxToFill, "maxToFill", 0);
            Scribe_Values.Look(ref selectedAreaId, "selectedAreaId", -1);
            Scribe_Values.Look(ref selectedAreaLabel, "selectedAreaLabel");
            // 型號被移除時 Scribe_Defs 會留下 null，ActiveOption 會自動退回其他可用選項。
            Scribe_Defs.Look(ref selectedKind, "selectedKind");
            Scribe_Collections.Look(ref spawnedPawns, "spawnedPawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                spawnedPawns ??= new List<Pawn>();
                spawnedPawns.RemoveAll(x => x == null);
                // 存檔裡的型號可能已經不在 Def 清單裡（mod 更新／移除），直接清掉走預設值。
                if (selectedKind != null && !ContainsKind(SpawnOptions, selectedKind))
                {
                    selectedKind = null;
                }
            }
        }
        // 1.6 的 tick 分派：
        //   tickerType=Normal → 每 tick 呼叫 Tick()（→ CompTick），
        //                       另外每隔 UpdateRateTicks 再呼叫一次 TickInterval(累積 delta)（→ CompTickInterval）。
        //   tickerType=Rare   → 只呼叫 TickRare()（→ CompTickRare），完全收不到 TickInterval。
        //   tickerType=Long   → 只呼叫 TickLong()（→ CompTickLong）。
        // 另外 Pawn.Tick() 自己還會每 250 tick 補叫一次 TickRare()。
        //
        // 舊版 CompTick / CompTickRare 都直接轉呼叫 CompTickInterval，於是：
        //   Rare 建築（DMS 各種平台）正確 = 1 倍；
        //   Normal 的機兵載具每 250 tick 收到 250(CompTick) + 250(TickInterval) + 250(補叫的 TickRare)
        //   = 3 倍速，冷卻與自動部署間隔全部只有 XML 寫的三分之一。
        // 現在統一：實際邏輯只放在 CompTickInterval，其餘入口只在「該 ticker 真的收不到 TickInterval」時補。

        public override void CompTick()
        {
            // Normal ticker 已經會另外收到 CompTickInterval，這裡不能再轉一次。
        }

        public override void CompTickRare()
        {
            // 只有 Rare ticker 真的靠這條路徑；Normal 的 Pawn 也會被叫到，那次必須忽略。
            if (parent?.def?.tickerType == TickerType.Rare)
            {
                CompTickInterval(250);
            }
        }

        public override void CompTickLong()
        {
            if (parent?.def?.tickerType == TickerType.Long)
            {
                CompTickInterval(2000);
            }
        }

        private int autoDeployTicks = 0;
        private bool autoDeployEnabled = false;
        // 換圖之後無人機不一定和平台同一刻落地，連推幾次確保都吃到限制。
        private int areaPushesRemaining = 0;

        public override void CompTickInterval(int delta)
        {
            Map curMap = parent?.Map;
            if (curMap != null && curMap != lastAppliedMap)
            {
                lastAppliedMap = curMap;
                areaPushesRemaining = 4;
            }
            if (areaPushesRemaining > 0)
            {
                areaPushesRemaining--;
                PushAreaToPawns(applyUnrestricted: false);
            }

            // �ֳt�ˬd�O�_�ݭn�B�z�N�o
            if (cooldownTicksRemaining > 0)
            {
                cooldownTicksRemaining -= delta;
                return;
            }
            // �۰ʳ��p�޿�
            if (autoDeployEnabled)
            {
                if (autoDeployTicks > 0)
                {
                    autoDeployTicks -= delta;
                    return;
                }
                // �ˬd�O�_�i�H�ͦ��s���
                if (spawnedPawns != null && spawnedPawns.Count < MaxPawnsPerDeploy * 2 && CanSpawn.Accepted)
                {
                    autoDeployTicks = CooldownTicks * 2; // �ϥξ�ƭ��k�N���B�I��
                    TrySpawnPawns();
                }
            }
            CleanupSpawnedPawns();
        }
        /// <summary>把容器補滿到 maxIngredientCount。只給 DEV gizmo 用。</summary>
        protected void DevFill()
        {
            ThingDef def = Props?.fixedIngredient;
            if (def == null || innerContainer == null)
            {
                return;
            }
            int stackLimit = Mathf.Max(1, def.stackLimit);
            int guard = 0;
            // 容器吃不下時 IngredientCount 不會前進，沒有這個保底就會卡在 while 裡把遊戲鎖死。
            while (IngredientCount < Props.maxIngredientCount && guard++ < 1000)
            {
                int stackCount = Mathf.Min(Props.maxIngredientCount - IngredientCount, stackLimit);
                Thing thing = ThingMaker.MakeThing(def);
                thing.stackCount = stackCount;
                // TryAdd 回傳實際吃下的數量；沒吃完的部分仍留在 thing 上且不屬於任何容器。
                if (innerContainer.TryAdd(thing, stackCount) < stackCount)
                {
                    if (!thing.Destroyed && thing.holdingOwner == null)
                    {
                        thing.Destroy();
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 把超出 maxIngredientCount 的材料吐出來。
        ///
        /// 舊版有三個問題：比較的是 innerContainer.Count（堆疊「數量」）而不是材料總數，
        /// 所以條件實際上永遠不成立、整個方法等於死碼；真的跑起來的話，
        /// SplitOff 出來的物件並不在容器裡，TryDrop 會記錯誤，
        /// 而且 TryDrop 失敗時 excess 不減、innerContainer[0] 也沒變，直接無限迴圈。
        /// 這裡改成照材料總數算、由後往前用 Take，並共用 DropIngredient 的落地邏輯。
        /// </summary>
        public void ReleaseOverFilled()
        {
            if (innerContainer == null || Props?.fixedIngredient == null)
            {
                return;
            }
            int excess = IngredientCount - Props.maxIngredientCount;
            if (excess <= 0)
            {
                return;
            }
            int guard = 0;
            for (int i = innerContainer.Count - 1; i >= 0 && excess > 0 && guard++ < 1000; i--)
            {
                if (i >= innerContainer.Count)
                {
                    continue;
                }
                Thing thing = innerContainer[i];
                if (thing == null || thing.Destroyed || thing.stackCount <= 0 || thing.def != Props.fixedIngredient)
                {
                    continue;
                }
                Thing taken = innerContainer.Take(thing, Mathf.Min(thing.stackCount, excess));
                if (taken == null)
                {
                    continue;
                }
                excess -= taken.stackCount;
                // 平台不在地圖上（運輸中）時沒有地方可以掉，DropIngredient 會直接銷毀 ——
                // 總比讓容器永遠超量、AmountToAutofill 一直算錯來得好。
                DropIngredient(taken);
            }
        }
    }

    public class CompProperties_MechPlatform : CompProperties
    {
        [NoTranslate]
        public string gizmoIconPath = "UI/Gizmos/ReleaseWarUrchins";

        [NoTranslate]
        public string gizmoIconPath_Retract = "UI/Drone_Retract";

        public ThingDef fixedIngredient;

        public int costPerPawn;

        public int maxIngredientCount;

        public int startingIngredientCount;

        /// <summary>
        /// 單一型號的舊欄位。仍然完全有效：
        /// 沒寫 spawnPawnKinds 時它就是唯一可部署的型號；兩個都寫時它排在清單最前面當預設值。
        /// </summary>
        public PawnKindDef spawnPawnKind;

        /// <summary>
        /// 可部署的型號清單。留空 = 沿用 spawnPawnKind 的舊行為，介面上不會多出任何按鈕。
        /// 填了兩種以上時，平台會多一個「部署型號」gizmo 讓玩家切換。
        ///
        /// 兩種寫法都吃：
        ///   &lt;li&gt;My_Drone&lt;/li&gt;
        ///   &lt;li&gt;&lt;pawnKind&gt;My_Drone&lt;/pawnKind&gt;&lt;costPerPawn&gt;50&lt;/costPerPawn&gt;&lt;/li&gt;
        ///
        /// 刻意用包裝類別而不是 List&lt;PawnKindDef&gt;：List&lt;Def&gt; 的交叉引用解析失敗時
        /// 條目會被靜默刪掉、整份清單塌陷；包成物件之後失敗的只會是 pawnKind 欄位為 null，
        /// ConfigErrors 抓得到，執行期也只跳過那一筆。
        /// </summary>
        public List<MechPlatformSpawnOption> spawnPawnKinds;

        /// <summary>
        /// 非玩家陣營是否每批隨機挑一種型號（權重取自各型號的 selectionWeight）。
        /// 關掉的話 NPC 一律用清單裡第一個型號。
        /// </summary>
        public bool npcRandomKind = true;

        private MechPlatformSpawnOption legacyOption;

        /// <summary>把舊的 spawnPawnKind 包成一個型號設定，讓新舊兩條路徑共用同一套邏輯。</summary>
        public MechPlatformSpawnOption LegacyOption
        {
            get
            {
                if (spawnPawnKind == null)
                {
                    return null;
                }
                // Def 載入是單執行緒，這裡的延遲建立不需要額外同步。
                if (legacyOption == null || legacyOption.pawnKind != spawnPawnKind)
                {
                    legacyOption = new MechPlatformSpawnOption { pawnKind = spawnPawnKind };
                }
                return legacyOption;
            }
        }

        public int cooldownTicks = 900;

        public int maxPawnsToSpawn = 3;

        /// <summary>
        /// 非玩家陣營的平台是否自行按冷卻計時投放。
        /// 設成 false 時 NPC 平台只會被填滿，實際投放時機交給思考樹節點決定。
        /// </summary>
        public bool npcAutoDeploy = true;

        public EffecterDef spawnEffecter;

        public EffecterDef spawnedMechEffecter;

        public bool attachSpawnedEffecter;

        public bool attachSpawnedMechEffecter;

        public CompProperties_MechPlatform()
        {
            compClass = typeof(CompMechPlatform);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string item in base.ConfigErrors(parentDef))
            {
                yield return item;
            }

            string who = parentDef?.defName ?? "(unknown)";

            if (fixedIngredient == null)
            {
                yield return $"{who}: CompProperties_MechPlatform needs a fixedIngredient.";
            }
            if (costPerPawn <= 0)
            {
                yield return $"{who}: CompProperties_MechPlatform.costPerPawn must be greater than 0 (got {costPerPawn}); it is the fallback cost for every spawn option.";
            }
            if (maxPawnsToSpawn <= 0)
            {
                yield return $"{who}: CompProperties_MechPlatform.maxPawnsToSpawn must be greater than 0 (got {maxPawnsToSpawn}).";
            }
            if (spawnPawnKind == null && spawnPawnKinds.NullOrEmpty())
            {
                yield return $"{who}: CompProperties_MechPlatform needs either spawnPawnKind or a non-empty spawnPawnKinds list.";
            }

            if (spawnPawnKinds != null)
            {
                HashSet<PawnKindDef> seen = new HashSet<PawnKindDef>();
                for (int i = 0; i < spawnPawnKinds.Count; i++)
                {
                    MechPlatformSpawnOption o = spawnPawnKinds[i];
                    if (o == null)
                    {
                        yield return $"{who}: spawnPawnKinds[{i}] is null.";
                        continue;
                    }
                    // 解析失敗的 pawnKind 在這裡就是 null；不報出來的話玩家只會看到「按鈕不見了」。
                    if (o.pawnKind == null)
                    {
                        yield return $"{who}: spawnPawnKinds[{i}] has no pawnKind (typo, or the defining mod is missing).";
                        continue;
                    }
                    if (!seen.Add(o.pawnKind))
                    {
                        yield return $"{who}: spawnPawnKinds lists {o.pawnKind.defName} more than once; only the first entry is used.";
                    }
                    if (o.costPerPawn == 0 || o.costPerPawn < -1)
                    {
                        yield return $"{who}: spawnPawnKinds[{i}] ({o.pawnKind.defName}) has an invalid costPerPawn of {o.costPerPawn}; use -1 to inherit.";
                    }
                    if (o.selectionWeight <= 0f)
                    {
                        yield return $"{who}: spawnPawnKinds[{i}] ({o.pawnKind.defName}) has a non-positive selectionWeight; NPC platforms will never pick it.";
                    }
                    int effectiveCost = o.costPerPawn > 0 ? o.costPerPawn : costPerPawn;
                    if (effectiveCost > 0 && maxIngredientCount > 0 && maxIngredientCount < effectiveCost)
                    {
                        yield return $"{who}: spawnPawnKinds[{i}] ({o.pawnKind.defName}) costs {effectiveCost} but maxIngredientCount is only {maxIngredientCount}; it can never be deployed.";
                    }
                }
            }
        }
    }

    /// <summary>
    /// 一種可部署的無人機型號。除了 pawnKind 之外的欄位都可省略，
    /// 省略時沿用 CompProperties_MechPlatform 上的對應設定。
    /// </summary>
    public class MechPlatformSpawnOption
    {
        public PawnKindDef pawnKind;

        /// <summary>單台成本。-1 = 沿用 Props.costPerPawn。</summary>
        public int costPerPawn = -1;

        /// <summary>單次最多放幾台。-1 = 沿用 Props.maxPawnsToSpawn。</summary>
        public int maxPawnsToSpawn = -1;

        /// <summary>投放後的冷卻。-1 = 沿用 Props.cooldownTicks（0 是合法值：不冷卻）。</summary>
        public int cooldownTicks = -1;

        /// <summary>這個型號的部署鈕圖示。留空 = 沿用 Props.gizmoIconPath。</summary>
        [NoTranslate]
        public string gizmoIconPath;

        /// <summary>選單顯示名稱。留空 = 用 pawnKind 的 label。</summary>
        public string label;

        /// <summary>選單顯示複數名稱。留空 = 用 pawnKind 的 labelPlural。</summary>
        public string labelPlural;

        /// <summary>需要的研究。只擋玩家陣營，未完成時選單裡會是灰項。</summary>
        public ResearchProjectDef requiredResearch;

        /// <summary>NPC 隨機挑型號時的權重。</summary>
        public float selectionWeight = 1f;

        public string Label => !label.NullOrEmpty() ? label : (pawnKind?.label ?? pawnKind?.defName ?? string.Empty);

        public string LabelPlural
        {
            get
            {
                if (!labelPlural.NullOrEmpty())
                {
                    return labelPlural;
                }
                string plural = pawnKind?.labelPlural;
                return plural.NullOrEmpty() ? Label : plural;
            }
        }

        public override string ToString()
        {
            return $"MechPlatformSpawnOption({pawnKind?.defName ?? "null"})";
        }

        /// <summary>
        /// 讓 &lt;li&gt;DefName&lt;/li&gt; 的簡寫和完整寫法都能用。
        ///
        /// 有了這個方法，DirectXmlToObject 就完全交給我們解析，所以每個欄位都要自己處理；
        /// 認不得的節點會報錯而不是靜默忽略 —— 打錯欄位名比欄位沒生效好抓太多。
        /// </summary>
        public void LoadDataFromXmlCustom(XmlNode xmlRoot)
        {
            if (xmlRoot == null)
            {
                return;
            }

            string mayRequire = xmlRoot.Attributes?["MayRequire"]?.Value?.ToLower();
            string mayRequireAny = xmlRoot.Attributes?["MayRequireAnyOf"]?.Value?.ToLower();

            // 簡寫：<li>My_Drone</li>
            if (xmlRoot.ChildNodes.Count == 1 && xmlRoot.FirstChild != null && xmlRoot.FirstChild.NodeType == XmlNodeType.Text)
            {
                string shorthand = xmlRoot.InnerText?.Trim();
                if (!shorthand.NullOrEmpty())
                {
                    DirectXmlCrossRefLoader.RegisterObjectWantsCrossRef(this, nameof(pawnKind), shorthand, mayRequire, mayRequireAny);
                }
                return;
            }

            foreach (XmlNode node in xmlRoot.ChildNodes)
            {
                if (node == null || node.NodeType != XmlNodeType.Element)
                {
                    continue;
                }
                string nodeMayRequire = node.Attributes?["MayRequire"]?.Value?.ToLower() ?? mayRequire;
                string nodeMayRequireAny = node.Attributes?["MayRequireAnyOf"]?.Value?.ToLower() ?? mayRequireAny;
                string raw = node.InnerText?.Trim();

                switch (node.Name)
                {
                    case nameof(pawnKind):
                        if (!raw.NullOrEmpty())
                        {
                            DirectXmlCrossRefLoader.RegisterObjectWantsCrossRef(this, nameof(pawnKind), raw, nodeMayRequire, nodeMayRequireAny);
                        }
                        break;
                    case nameof(requiredResearch):
                        if (!raw.NullOrEmpty())
                        {
                            DirectXmlCrossRefLoader.RegisterObjectWantsCrossRef(this, nameof(requiredResearch), raw, nodeMayRequire, nodeMayRequireAny);
                        }
                        break;
                    case nameof(costPerPawn):
                        costPerPawn = ParseInt(raw, costPerPawn, node.Name);
                        break;
                    case nameof(maxPawnsToSpawn):
                        maxPawnsToSpawn = ParseInt(raw, maxPawnsToSpawn, node.Name);
                        break;
                    case nameof(cooldownTicks):
                        cooldownTicks = ParseInt(raw, cooldownTicks, node.Name);
                        break;
                    case nameof(selectionWeight):
                        selectionWeight = ParseFloat(raw, selectionWeight, node.Name);
                        break;
                    case nameof(gizmoIconPath):
                        gizmoIconPath = raw;
                        break;
                    case nameof(label):
                        label = raw;
                        break;
                    case nameof(labelPlural):
                        labelPlural = raw;
                        break;
                    default:
                        Log.Error($"[Fortified] MechPlatformSpawnOption: unknown field <{node.Name}> in <{xmlRoot.Name}>. Supported: pawnKind, costPerPawn, maxPawnsToSpawn, cooldownTicks, gizmoIconPath, label, labelPlural, requiredResearch, selectionWeight.");
                        break;
                }
            }
        }

        private static int ParseInt(string raw, int fallback, string fieldName)
        {
            if (!raw.NullOrEmpty() && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                return value;
            }
            Log.Error($"[Fortified] MechPlatformSpawnOption: could not parse <{fieldName}> value \"{raw}\" as an integer; keeping {fallback}.");
            return fallback;
        }

        private static float ParseFloat(string raw, float fallback, string fieldName)
        {
            if (!raw.NullOrEmpty() && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                return value;
            }
            Log.Error($"[Fortified] MechPlatformSpawnOption: could not parse <{fieldName}> value \"{raw}\" as a number; keeping {fallback}.");
            return fallback;
        }
    }
}
