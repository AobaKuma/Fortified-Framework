# Fortified — 類別與功能總覽

此文件依資料夾彙整 Fortified 專案中主要類別/模組的職責與實作機制（精簡版）。

> 專案位置：StandaloneFunctions、Mech、Thing、Components、Hediff、Utility、Patch、Structures、Ability、World、UI 等。

## 目錄
- StandaloneFunctions
  - AirSupport
  - AmmoSwitch
  - Modification
  - MultiTurrets
  - PlateArmorVest
  - BiochemicalProtection
  - ScoutedRaid
  - WorkTableAutonomous / EnvironmentalBill
  - 其他（Bossgroup、SignalAreaTrigger、ForceTargetable、PawnReplace...）
- Mech（機甲子系統）
- Thing / Projectile / Explosions
- Thing / MechHolders / DeployableItem / Containers
- Hediff / 改造 / 疾病
- Components（雜項 Comp）
- Utility / Helpers / StatWorkers / UI 幫助類
- Patch（Harmony）

---

## StandaloneFunctions

### AirSupport
- 關鍵類別:
  - AirSupportComp (抽象) — 基底介面，提供 DrawHighlight() 與 Trigger()，多種具體實作 (Bombard/Strafe/StrafeII/Sound/etc.)
  - AirSupportComp_Bombard / AirSupportComp_Strafe / AirSupportComp_StrafeII — 不同空援行為的具體實作，會建立對應的 AirSupportData 放入排程
  - AirSupportData (抽象) — 可序列化、延遲觸發的工作項目，包含 map/target/triggerTick/triggerer
  - AirSupportData_LaunchProjectile / _LaunchProjectileOnEdge / _LaunchProjectileFromPlane — 負責生成並以 launcher 發射 Projectile（包含 CE 兼容 hook）
  - AirSupportData_SpawnFlyBy / AirSupportData_SpawnThing — 生成 FlyByThing 並可附帶其他 AirSupportData
  - AirSupportData_Sound / AirSupportData_Effecter — 觸發音效與 effecter
  - FlyByThing — 自定的天空飛越物件，控制精確位置、陰影與逐 tick 移動，支援被附加的 LaunchFromPlane 資料
  - Bullet_ForAirSupport — 對飛越彈藥的命中選擇/覆蓋機制，讓飛行彈可命中地面目標或穿過
  - GameComponent_CAS — 全域排程器，維護按 triggerTick 排序之 AirSupportData 列表並在到點時觸發

機制與資料流程：
  - 當玩家 / 設備呼叫某個 AirSupportDef 時，對應的 AirSupportComp.Trigger() 會建立一或多個 AirSupportData（如 LaunchProjectile、SpawnFlyBy、Sound）並呼叫 GameComponent_CAS.AddData()
  - GameComponent_CAS 會於每個 GameComponentTick 檢查 datas，當 triggerTick 到達則呼叫 data.Trigger()，執行實際生成／發射／播放音效等邏輯。
  - LaunchProjectile 類別內處理 spawn、找到適合的 launcher（triggerer 或虛擬 Pawn）並啟用 CE 管線（若存在）或原版 Launch。
  - SpawnFlyBy 類別可將附屬的 LaunchProjectile 類別與已生成的 FlyByThing 關聯 (IAttachedToFlyBy)，使飛機在飛越時發射彈藥或播放音效。

繪製/提示：AirSupportComp 的 DrawHighlight() 用於游標/範圍提示（例如 DrawRadiusRing）。

### AmmoSwitch
- 關鍵類別與檔案：
  - CompProperties_AmmoSwitch — 在 def 中定義可切換的 AmmoOption 列表、defaultIndex、switchCooldown、soundSwitch
  - CompAmmoSwitch — ThingComp 實作，負責維護 selectedIndex / switchingToIndex / cooldownUntilTick，並提供：
    - CurrentAmmo / CurrentProjectile / IsUsingDefaultProjectile：查詢當前彈種與是否使用 base verb 的預設投射物
    - QueueSwitchJob(Pawn pawn, int idx)：為 pawn 排隊一個切換彈藥的 Job (FFF_SwitchAmmo)
    - SetAmmo(int index, bool startCooldown)：直接切換並可啟動冷卻
    - GetSwitchGizmo(Thing user)：產生可點選的 Command_Action（或 FloatMenu）用於 UI 切換
    - CompMultiSelectFloatMenuOptions 支援多選情境下為多個物件排程工作
    - GetStatFactor / GetStatsExplanation：對 Accuracy 類 Stat 提供彈種乘數影響
  - AmmoOption (資料型) — 定義單一彈種的顯示標籤、icon、projectileDef、accuracyFactor、description 等
  - JobDriver_SwitchAmmo (job) — pawn 實際執行切換（由 Comp 透過 QueueSwitchJob 觸發）

機制與互動：
  - UI：Gizmo 與 FloatMenu 選單列出 AmmoOption（包含特殊 -1 表示使用 verb 的預設投射物），可對單個或多個物件下達切換命令。
  - 行為：若由 pawn 執行切換，Comp 會存 switchingToIndex，JobDriver 讀取並在完成時呼叫 SetAmmo。
  - 效果：切換可能改變 Weapon 的 projectile（CurrentProjectile），並影響射擊時的命中/準確度（GetStatFactor）與 tooltip/說明文字。

Patch/整合：相關 Patch 補入 turret / pawn equipment 的 gizmo，使 UI 在合適位置顯示切換選項。

### Modification
- HediffComp_Modification, CompTargetable_AddHediffOnTarget
- JobDriver_ApplyModification / JobDriver_RemoveModification
- ModificationUtility, FloatMenuOptionProvider_AdjustModify

功能要點：改造系統（以 Hediff 與 Comp 表示改造效果），包含 UI 與工作流程。

### MultiTurrets
- CompMultipleTurretGun, CompPropertiesMultipleTurretGun
- TurretGizmos, PawnRenderNode_SubTurretGun, PawnRenderNodeWorker_SubTurretGun

功能要點：多子炮塔管理與繪製，提供 UI 操作與射擊協調。

### PlateArmorVest
- CompBulletproofPlate, IReplenishable, JobDriver_Replenish, Gizmo_BulletproofPlateStatus

功能要點：防彈板/護甲補給機制（Comp + JobDriver + Gizmo）。

### BiochemicalProtection
- BiochemicalProtectionExtension (ModExtension)
- Patch_CanBeInfected, Patch_DiseaseContractChanceFactor

功能要點：感染 / 生化防護機制，透過 Patch 攔截遊戲感染流程並以 ModExtension 設定特性。

### ScoutedRaid
- 核心類別：
  - FFF_ScoutedRaidController (GameComponent) — 中央調度器，持有並 tick 管理 ScoutedRaidJob 列表；在 GameComponentTick 中呼叫 ScoutedRaidStateMachine.Tick(job)
  - ScoutedRaidJob / ScoutedRaidStateMachine / ScoutedRaidPhase — 表示單一偵查突襲任務、狀態機以及階段定義（tick 型態的非同步流程）
  - BombardmentDispatcher、FlareSlotBuilder、MainRaidIssuer 等 — 分別負責火力配置、照明 flare 時間格與實際發起 raid 的邏輯

運作模式：
  - Module 將 ScoutedRaidJob 註冊到 FFF_ScoutedRaidController；Controller 每 tick 迭代 job 副本並呼叫 StateMachine 以避免同步變動造成錯誤。
  - StateMachine 管理階段遷移、觸發對應子系統（例如透過 GameComponent_CAS 添加入空援資料或召喚實體），最終將 job 標記為 Done 並由 Controller 清除。

### WorkTableAutonomous / EnvironmentalBill
- Building_WorkTableAutonomous, WorkGiver_DoAutonomousBill
- JobDriver_DoAutonomousBill / JobDriver_FinishAutonomousBill
- ModExtension_AutoWorkTable, ModExtension_QualityChance

功能要點：自動工作台系統與環境帳單類型，自動化生產流程。

### BuildingReplace（生成時機率替換建築）
- 關鍵類別:
  - ModExtension_ReplaceBuilding (StandaloneFunctions/BuildingReplace/) — 掛在 ThingDef 上，描述「這個建築生成時有多少機率換成清單中的另一個 ThingDef」；欄位含 replaceChance、replacements(thing/weight/stuff)、onlyDuringMapGen、requireSameSize、inheritFaction/Stuff/HitPointsPercent，並提供 TryPickReplacement 與 ValidationErrors。
  - Patch_ReplaceBuilding — Prefix 攔 GenSpawn.Spawn(7 參數版)，改寫 ref newThing；預設僅在 MapGenerator.mapBeingGenerated == map 期間作用，非生成期以單一靜態欄位比較即早退，並快取 def→extension 查表。
  - ReplaceBuildingUtility — 材質合法性判定 / ResolveStuff / TryMakeReplacement / 血量百分比沿用；任何步驟失敗一律回傳 null 保留原建築。
  - ReplaceBuildingValidator — [StaticConstructorOnStartup]，啟動時建快取並輸出設定錯誤警告。
- 與 PawnReplace 的差異：PawnReplace 依 Site 威脅點數的 FloatRange 對應 PawnKindDef；本子系統是對建築的權重機率替換。替換不連鎖。

### 其他小型子系統
- Bossgroup（CompUseEffect_SummonRaid）、SignalAreaTrigger、ForceTargetable、PawnReplace 等，皆以 Comp / ModExtension / Patch 組合驅動特定互動或事件。

---

## Mech（機甲子系統）
關鍵類別與檔案：
  - CompDrone (Mech/Drone/CompDrone.cs)
    - 一次性遙控 / 可回收的 Drone Pawn 的 ThingComp；支援「平台」（platform）概念（建築或穿戴式 apparel 作為控制來源）、返回平台、充電檢查與 Draft 控制。
    - 提供 Gizmo（Return）、Queue/Job 支援、與 PawnComponentsUtility 與 DraftController 的 Harmony patch 整合以顯示 Draft gizmo。
  - CompDronePack (Mech/Drone/CompDronePack.cs)
    - 作為 wearable pack 的 AI 可使用入口（CompAIUsablePack），在特定條件下自動觸發 wearable 的 verb。
  - CompMechPlatform (Mech/Drone/CompMechPlatform.cs)
    - 機甲載體/平台（IThingHolder），內含固定材料容器(innerContainer)、生成 pawn 的邏輯、autodeploy 與 cooldown 機制。
    - TrySpawnPawns() 會消耗固定材料（fixedIngredient）並以 PawnGenerationRequest 產生機甲 pawn；spawned pawns 可被記錄與收回（Retract）。
    - 提供多個 Gizmo（Deploy, Retract, AutoDeploy toggle, Area selection）、FloatMenu 選項與多人同步的同步點（MULTIPLAYER 標註）。
  - Building_MechCapsule / MechCapsuleUtility / ModExtension_MechCapsule
    - 管理機甲膠囊與停用機甲的儲存與取出流程（包含 JobDriver_HackMechCapsule、JobDriver_OpenMech 等）。
  - CompMechApparel / MechApparelGenerator / HumanlikeMechApparelUtility
    - 支援機甲服裝/外觀系統，負責在生成或繪製時處理機甲的外觀與服裝配件。
  - HumanlikeMech (Mech/HumanlikeMech/HumanlikeMech.cs)
    - 繼承 Pawn，代表人型機甲，支援 Mech 專屬 extension（HumanlikeMechExtension），初始化 story/style/skills、處理 head/body graphic 與載具專屬欄位。
  - WeaponUsableMech / IWeaponUsable / MechWeaponExtension
    - 讓機甲 Pawn 能配戴、裝備武器並由遊戲標準流程操作；MechWeaponExtension 定義武器對 mech 的額外配置或行為。
  - Painting 子系統 (Mech/Painting/*)
    - CompPaintable、PawnRenderNode_Painting、ShaderParamBuilder、JobDriver_PaintMech、UI/ITab_Mech_Gear 與相關 Def 用於機甲上色/涂裝流程與 UI。

補充機制與互動：
  - Spawn/Resource：CompMechPlatform 以固定材料計數 (fixedIngredient / costPerPawn) 決定可 spawn 的 pawn 數量，並在 spawn 後扣除材料。
  - Retract 與 收回：Spawned pawns 被記錄，Retract 會嘗試將 pawn 返回平台並補回材料（或放置於地面），並有 cooldown 與 auto-deploy 行為。
  - Draft / Control：CompDrone 與其他 mech 類別透過多個 Patch 整合到 Drafter/MechanitorUtility，以決定能否被玩家下令、顯示 gizmo 與限制行為。
  - Jobs / AI：提供多個 JobDriver（RepairSelf、ResurrectMech、ReturnToPlatform、RepairSelf、MechLeave 等）與 ThinkNode/JobGiver，用來控制 mech 的行為與自動維護。
  - Multiplayer 支援：在多處使用 MULTIPLAYER 同步標註（[SyncMethod] / Sync wrappers）以協調多人模式下的行為。

相關 Patch（整合點）：
  - Patch_IsColonyMechPlayerControlled / Patch_CanControlMechs / Patch_Pawn_DraftController_ShowDraftGizmo / Patch_Pawn_IsColonistPlayerControlled / Patch_Pawn_DropAndForbidEverything / Patch_Pawn_CanTakeOrder 等
    - 這些 Harmony Patch 負責將 mech 的控制權、繪製、下達命令與裝備/丟棄邏輯整合進原遊戲流程。 

功能要點總結：
  - 機甲子系統跨越生成/資源管理、玩家控制、外觀/上色、武器使用與 AI 行為；以 Comp、Pawn 擴充、JobDriver 與 Harmony Patch 的結合來實作完整的 mech 生態。

---

## Thing / Projectile / Explosions
- Projectile_Parabola、Projectile_AlongWayDamage、Projectile_ClusterBomb、Projectile_ConeExplosive、Projectile_ExplosiveByComps 等
- ModExtension_CompositeExplosion、ModExtension_AlongWayDamage、ModExtension_ExpolsionWithConditions
- CompExplosiveWithComposite、CompAfterBurner

功能要點：自定彈道、沿途傷害、複合爆炸與條件化爆炸效果，資料驅動的 ModExtension 與 Comp 結合。

---

## Thing / MechHolders / DeployableItem / Containers
- Building_MechCapsule、Building_DeactivatedMech、MechCapsuleUtility、ModExtension_MechCapsule
- CompMinifyToInventory、DeployUtility、MinifiedThingDeployable、Building_ListedContainer、CompListedContainer、Building_SurplusContainer
詳細子系統 — Deployable（可部署物品）:
  - CompMinifyToInventory (Thing/DeployableItem/CompMinifyToInventory.cs)
    - 作為 CompUseEffect，將目標物件（建築或物品）最小化並嘗試放入使用者的裝備欄或背包；若可裝備且 pawn 無主要武器則會裝備，否則加入 inventory。
    - 會處理 minified building 的情形並播放互動音效。
    - 迷你化結果若是裝備類型（有 CompEquippable 且 equipmentType != None），先跑 EquipmentUtility.CanEquip 完整資格檢查（涵蓋生物編碼、靈魂鏈接綁定、意識形態角色限制，以及 Patch_EquipmentUtility_CanEquip 疊加的 FFF_NeverEquip、HeavyEquippableExtension 體型門檻與 IWeaponUsable 的 MechWeaponExtension 白名單）；不合格則改收進物品欄。
    - IWeaponUsable（機械體）額外套用 MassUtility 載重上限；收納失敗時以 GenPlace.TryPlaceThing 落地兜底，確保物件不會遺失。
  - DeployUtility / MinifiedThingDeployable (Thing/DeployableItem/MinifiedThingDeployable.cs)
    - 提供 Pawn 在持有 MinifiedThing（MinifiedThingDeployable）時的 Gizmo/Command_Target 用於選定放置格。
    - DeployUtility.CanOperateDeployable 定義操作資格（IWeaponUsable 免除 ToolUser 智力門檻）；IsAcceptedDeployCell 與 TargetParam 定義可放置的格子範圍/驗證（與 pawn 四方相鄰、在地圖內、該格沒有 edifice）；CanAutoManTurret 決定部署後是否自動接手操作（機械體受 TurretMannableExtension 白名單限制）。
    - Deploy() 流程：檢查佔位與地圖邊界、擦除現存物件(GenSpawn.WipeExistingThings)、調用 DeployCECompatHook 以支援兼容性，設置派系（若可），然後 GenSpawn.Spawn 實體；生成失敗時直接中止而不銷毀外殼，避免內容物遺失。
    - Deploy 由 [SyncMethod] SyncedDeploy 包裝以支援多人模式；選取狀態變更僅在本地實際選中該 pawn 時執行。
    - MinifiedThingDeployable 提供 IGizmoGiver 接口，可為 pawn 返回可放置的 Gizmo（GetGizmoForPawn），並包含 MinifiedThingDeployableGraphicExt 以在未放置時自定義顯示資源。
  - Patch_CompUsable_CanBeUsedBy (Thing/DeployableItem/Patch_CompUsable_CanBeUsedBy.cs)
    - Transpiler：vanilla 的 CompUsable.CanBeUsedBy 以 RaceProps.IsFlesh 擋掉所有非血肉 pawn。此修補替換該次呼叫，僅對「IWeaponUsable + 目標掛有 CompMinifyToInventory」放行，不影響其他 CompUsable 物品。
    - 找不到目標 IL 時會 Log.Warning 並原封退回，不產生壞 IL。
  - Patch_Pawn_GetGizmos (Thing/DeployableItem/Patch_Pawn_GetGizmos.cs)
    - Harmony postfix：向 Pawn_InventoryTracker.GetGizmos 注入額外 Gizmo，若 inventory 或 equipment 中存在實作 IGizmoGiver 的項目，則呼叫其 GetGizmoForPawn 與界面顯示（資格判定走 DeployUtility.CanOperateDeployable，並要求玩家陣營、pawn Spawned 且被選中）。

機制要點：
  - 玩家互動：持有可部署的 MinifiedThing 時，Pawn 會在選取時顯示 Command_Target gizmo，點選後會執行 Deploy()，並在成功後由 pawn 自動 man turret（若適用）與銷毀 minified 物件。
  - 可裝備/加入背包：CompMinifyToInventory 支援將物件直接裝備至 pawn 的 equipment（若條件允許）或放入 inventory。
  - 機械體支援：vanilla 的 FloatMenuOptionProvider_FromThing 是 MechanoidCanDo => false，機械體看不到 CompUsable 選項，因此改由 FloatMenuOptionProvider_WeaponUsable → FloatMenuUtility 的「可回收的部署建築」分支補上。
  - 兼容鉤子：MinifiedThingDeployable.Deploy 會呼叫 DeployCECompatHook，供其他模組（例如 CE）注入兼容處理。
  - CE 設定同步（FortifiedCE/Harmony_DeployableTurretCompat.cs，DeployableCESync）：砲塔建築與迷你化武器是兩個獨立 Thing、各帶一份 CompAmmoUser / CompFireModes，因此在收起（MinifyUtility.MakeMinified postfix）與展開（DeployCECompatHook postfix）兩個方向都搬移彈匣內容、選定彈種、機會裝填閾值（CompAmmoUser.TryReloadOn，夾在 0~目標 MagSize）、連發模式、瞄準模式與瞄準部位（CompFireModes）。射擊模式只複製目標端可用清單內的值，並清除 CompFireModes.newComp 私有旗標以免 CE 稍後 ResetModes 覆蓋。兩型態彈匣容量不同（或目標彈匣已裝別種彈藥）時，裝不下的部分依 CE 的 AmmoDef.ammoCount / partialUnloadAmmoDef 規則換算回實體彈藥，交給 DeployContext.CurrentWorker 的 CompInventory，收不下才落地。
  - DeployContext（Thing/DeployableItem/DeployContext.cs）：轉換過程中來源與目標 Thing 都已脫離持有者也尚未生成，兼容層無從回推溢出物該給誰，因此由 CompMinifyToInventory 與 MinifiedThingDeployable.Deploy 以 using 區塊掛上執行者供查詢。

功能要點：部署物品的 UI/互動、放置檢查、spawn 流程、與 inventory/equipment 的整合，以及對外的兼容擴充點。

---

## Hediff / 改造 / 疾病
- HediffComp_*（SelfHeal、GiveHediff、RemoveHediffsOnDeath、ProtectiveShield 等）
- HediffGiver_RandomSilent、IngestionOutcomeDoer_GiveHediffIfNoBionic

功能要點：Hediff 相關行為模組化（施加、移除、治療、特殊效果），與改造系統整合。

---

## Components（雜項 Comp）
- CompFlyingFleckThrower、CompCastEffecter、CompCastFlecker、CompCastPushHeat
- CompFloating、CompPaintable、CompRandomColorOnSpawn、CompIncidentMaker、CompFueledSpawner、CompSignalTower
- CompSubRelay、CompCommandRelay、CompMechanitorRange 等

功能要點：視覺特效、事件生產、上色、燃料生產、信號與指令中繼等，均以 ThingComp 模式實作。

---

## Utility / Helpers / StatWorkers / UI 支援
- Utility
  - CheckUtility.cs（檢查/驗證工具）
  - WeaponTagUtil.cs（武器標籤處理）
  - FloatMenuUtility.cs、FFF_StructureUtility.cs、FleckMakerEx.cs
- StatWorker_*、IngredientValueGetter_* 等：提供統計/數值顯示或擴充

功能要點：跨模組共用函式、Gizmo/FloatMenu 支援、數值計算輔助。

---
