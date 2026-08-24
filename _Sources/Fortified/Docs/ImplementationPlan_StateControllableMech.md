# Fortified Framework — 狀態可控機械（State-Controllable Mech）實作執行方案

> 目標：把散落在各 Harmony patch 中的 Comp 查詢抽離、以「狀態可控介面」統一再控制判定、為基底機械類別加入快取 comps，並將 AMO（Artificial Military Organism，軍用人工生物，如 DMS 蛙人）實作為預設可控、不可修理但自體再生、以口糧補能量的子類別。
>
> 本文件所有結論均以 rimsearcher defs 查詢與 DecompilerServer 反編譯結果為依據，未驗證處標記 `[UNVERIFIED]`。

---

## 1. 設計意圖（來自交談）

1. **移除 patch 中的 Comp 計算**（至少大部分）——每次判定不再重複 `TryGetComp<T>()`。
2. **新增介面讓機械可「依狀態被控制」**，並讓這個介面成為 `IOverseer` 的基底——消除雙重檢查。
3. **在 `WeaponUsableMech` 與 `HumanlikeMech` 加入快取 comps**。
4. **AMO 成為子類別**，該介面的布林預設為 `true`，無需額外檢查。
5. **AMO 不可被修理，但自行再生**。
6. **消耗食物（口糧）補充能量**（或改用自訂 need）。

---

## 2. 現況盤點（證據）

### 2.1 現有介面與類別

| 類型 | 定義 | 位置 |
|---|---|---|
| `IOverseer` | `{ CompOverseer Comp { get; } }` | `_Sources\Fortified\Mech\Overseer\IOverseer.cs:10` |
| `IOverseerMech : IOverseer` | + `MinCharge` / `MaxCharge` / `WorkMode` / `Notify_NameChanged()` | 同上 `:15` |
| `Building_Overseer : Building, IOverseer` | 快取 `CompOverseer`（lazy getter） | `Overseer\Building_Overseer.cs:15,98` |
| `OverseerMech : WeaponUsableMech, IOverseerMech` | 快取 `CompOverseer` | `Overseer\OverseerMech.cs:61,71` |
| `HumanlikeOverseerMech : HumanlikeMech, IOverseerMech` | 快取 `CompOverseer` | 同上 `:174,184` |
| `WeaponUsableMech : Pawn, IWeaponUsable, ICaravanOwner` | **無任何快取 comp** | `Mech\WeaponUsable\WeaponUsableMech.cs:11` |
| `HumanlikeMech : Pawn, IWeaponUsable, ICaravanOwner` | **無任何快取 comp** | `Mech\HumanlikeMech\HumanlikeMech.cs` |
| `CompOverseer : ThingComp` | dummyPawn 機制、`canRepair`/`ticksPerHeal` props | `Overseer\CompOverseer.cs:117` |
| `CompDeadManSwitch : ThingComp` | `woken` 旗標；Overseer 以 `PawnRelationDefOf.Overseer` 關係取得 | `Mech\Awaken\CompDeadManSwitch.cs:15,38` |
| `CompCommandRelay : ThingComp` | 靜態 `allRelays` 清單 | `Mech\MechanitorCommand\CompCommandRelay.cs:10` |
| `CompDrone : ThingComp` | 一次性遙控機械 | `Mech\Drone\CompDrone.cs:16` |

### 2.2 patch 中現存的 Comp 查詢（要消除的目標）

| Patch | 查詢 | 檔案 |
|---|---|---|
| `Patch_CompOverseerSubject_State` | `parent is IOverseer` + `TryGetComp<CompDeadManSwitch>()` | `Mech\Patches\Patch_CompOverseerSubject_State.cs:16,22` |
| `Patch_InMechanitorCommandRange` | `is IOverseer`、`TryGetComp<CompDeadManSwitch>()`、`TryGetComp<CompCommandRelay>()`、`TryGetComp<CompDrone>()` | `Mech\Patches\Patch_InMechanitorCommandRange.cs:23,43,48,53` |
| `Patch_MechanitorUtility_CanDraftMech` | `is IOverseer`、`TryGetComp<CompDeadManSwitch>()`、`kindDef.race.HasComp(typeof(CompCommandRelay))` | `Mech\Patches\Patch_MechanitorUtility_CanDraftMech.cs:14,22,31` |
| `Patch_CanControlMechs` | `OverseenPawns?.Where(p => p.TryGetComp<CompCommandRelay>() != null)` | `Mech\Patches\Patch_CanControlMechs.cs:25` |
| `Patch_IsColonyMechPlayerControlled` | `is IWeaponUsable`、`TryGetComp<CompDrone>()` | `Mech\Patches\Patch_IsColonyMechPlayerControlled.cs:13,14` |
| `Patch_Pawn_DraftController_ShowDraftGizmo` | `is IOverseer`、`TryGetComp<CompDeadManSwitch>()` | `Mech\Patches\Patch_Pawn_DraftController_ShowDraftGizmo.cs:15,20` |
| `Patch_CanDropWeapon` | `is IWeaponUsable`、`TryGetComp<CompDrone>()`、`TryGetComp<CompVehicleWeapon>()` | `Mech\Patches\Patch_CanDropWeapon.cs:17,21,75` |
| `Patch_MechInteracte`（`Patch_Overseer.cs`） | `TryGetComp<CompDeadManSwitch>()` | `Mech\Patches\Patch_Overseer.cs:18` |
| `Patch_JobGiver_GetEnergy_Max/Min` | `is IOverseerMech` | `Mech\Patches\Overseer\Patch_JobGiver_GetEnergy_Max.cs:19` 等 |
| `JobGiver_RepairSelf` / `ThinkNode_Condition_Wake` | `TryGetComp<CompDeadManSwitch>()` | `Mech\Awaken\Job\*` |
| `JobGiver_RepairMechs_Overseer` | `pawn is IOverseer`、`thing.TryGetComp<CompMechRepairable>()` | `Mech\Overseer\Jobs.cs:17,44` |

> 特徵：同一隻機械在同一幀可能被多個 patch 各自 `TryGetComp` 一次；`CompDeadManSwitch` 的 `woken` 狀態尤其被重複查詢（State getter、CanDraftMech、ShowDraftGizmo、InMechanitorCommandRange）。

### 2.3 vanilla 判定鏈（反編譯證據）

- `OverseerSubjectState` 列舉：`RequiresOverseer` / `RequiresBandwidth` / `Overseen`（`Assembly-CSharp.dll`）。
- `CompOverseerSubject.State` getter：`Overseer?.mechanitor == null → RequiresOverseer`；`Overseer.mechanitor.ControlledPawns.Contains(Parent) → Overseen`；否則 `RequiresBandwidth`。
- `MechanitorUtility.EverControllable(mech)` = `mech.OverseerSubject != null`。
- `MechanitorUtility.CanControlMech(pawn, mech)`：要求 `pawn.mechanitor != null`、`mech.IsColonyMech`、非 downed/dead/attacking、`EverControllable`、`GetOverseer() == pawn`、頻寬足夠。
- `MechanitorUtility.InMechanitorCommandRange(mech, target)`：`mech.GetOverseer()` 同地圖且 `overseer.mechanitor.CanCommandTo(target)`。
- `Need_MechEnergy`：`MaxLevel = pawn.RaceProps.maxMechEnergy`；作用時每 day 掉 10、閒置 3；`CurLevel <= 0` → 自關機；`NeedInterval` 每 400 ticks 扣 `FallPerDay/400`。
- `NeedDef "MechEnergy"`：`playerMechsOnly = true` → `ShouldHaveNeed` 要求 `RaceProps.IsMechanoid && Faction == OfPlayer && OverseerSubject != null`。
- `MechRepairUtility.CanRepair(mech)`：需要 `TryGetComp<CompMechRepairable>() != null` 且有可治療 hediff 或缺武器。
- `CompMechRepairable`：僅含 `autoRepair` 旗標 + 開關 gizmo。
- `RaceProperties.IsMechanoid = FleshType == FleshTypeDefOf.Mechanoid`。

### 2.4 AMO 現況（DMS 蛙人 = 第一個 AMO）

- ThingDef `DMS_Mech_Frogman`：`thingClass = Fortified.HumanlikeMech`；`race.fleshType = Mechanoid`、`maxMechEnergy = 100`、`intelligence = ToolUser`、`hasMeat = true`、`foodType = None`、`needsRest = false`。
- comps：`DMS.CompRationMagazine`（rationDef = `DMS_CombatRation`，max 4）、`CompDeadManSwitch`、`CompInteracte`、`CompMechApparel`、`CompPaintable`、`CompMechRepairable`、`CompMechanoid`、`CompOverseerSubject`、`CompWakeUpDormant`（defs.db `--brief`）。
- 身體：`DMS_Frogman` — 人類內臟＋合成骨架，**無胃**，胃的位置改為 `DMS_Bioreactor`（生體反應爐，`MetabolismSource`，只接受直接投料的高密度口糧）。
- 已存在「口糧→能量＋再生」能力：`DMS_FrogmanRationRecovery`（`DMS.CompAbilityEffect_RationRecovery`）：消耗 1 份口糧 → `needs.energy.CurLevel = MaxLevel` + 移除非缺失部位的傷勢。
- DMS 端程式碼：`Dead-Man-Switch\_Source\DMS\Frogman\RationRecovery.cs`（`CompRationMagazine` / `JobDriver_LoadRation` / `CompAbilityEffect_RationRecovery`）。
- 框架端已有 `HediffComp_MechHeal`（`Hediff\HediffComp_SelfHeal.cs`）：每 `healIntervalTicksStanding` 對隨機 `Hediff_Injury` 扣 `healAmount` severity —— 可作為「自體再生」的現成零件。

---

## 3. 目標架構

### 3.1 新介面（狀態可控）

```csharp
// Mech\Overseer\IStateControllableMech.cs（新檔）
namespace Fortified;

/// <summary>
/// 依狀態被控制：實作者宣告自己在當前狀態下「是否可直接受玩家控制」。
/// 取代散落在各 patch 的 TryGetComp 判定。
/// </summary>
public interface IStateControllableMech
{
    /// <summary>目前是否可直接受控制（不需機械師、不需範圍、不需頻寬檢查）。</summary>
    bool ControllableByState { get; }
}
```

- `IOverseer : IStateControllableMech`（改 `IOverseer.cs`）：Overseer 天生即「狀態可控」，`ControllableByState => true` 由實作類別提供，或由介面提供 default（C# 8 default interface member，注意 IL 相容性 `[UNVERIFIED]` — 若下游 DMS.dll 以舊編譯器引用，建議改用抽象基底類別或直接在各實作類別實作屬性）。

### 3.2 基底類別加入快取 comps

`WeaponUsableMech` 與 `HumanlikeMech` 各自加入（lazy 快取，與現有 `OverseerMech.Comp` 相同模式）：

```csharp
private CompOverseerSubject cachedOverseerSubject;
private CompDeadManSwitch cachedDeadManSwitch;
private CompCommandRelay cachedCommandRelay;
private CompDrone cachedDrone;
private CompMechRepairable cachedMechRepairable;

public CompOverseerSubject OverseerSubjectComp => cachedOverseerSubject ??= GetComp<CompOverseerSubject>();
public CompDeadManSwitch DeadManSwitchComp => cachedDeadManSwitch ??= GetComp<CompDeadManSwitch>();
public CompCommandRelay CommandRelayComp => cachedCommandRelay ??= GetComp<CompCommandRelay>();
public CompDrone DroneComp => cachedDrone ??= GetComp<CompDrone>();
public CompMechRepairable MechRepairableComp => cachedMechRepairable ??= GetComp<CompMechRepairable>();
```

- 快取時機：`SpawnSetup` / `PostMake` 時預先抓取一次（comp 在 spawn 後不變），避免熱路徑上第一次 `??=` 的字典查詢。
- `Building_Overseer` 已示範此模式（`Comp` / `Power` lazy getter）。
- 空值語意保留：`GetComp<T>()` 對不存在的 comp 回傳 null，快取 null 即代表「該機械沒有此 comp」，語意不變。

### 3.3 patch 改寫：一律走介面＋快取

所有「可控性」patch 改為單一檢查點：

```csharp
// 範例：Patch_CompOverseerSubject_State 的 Postfix 改寫
if (__result == OverseerSubjectState.Overseen) return;
if (__instance.parent is IStateControllableMech sc && sc.ControllableByState)
{
    __result = OverseerSubjectState.Overseen;
}
```

各 patch 的對應替換：

| 原判定 | 替換為 |
|---|---|
| `parent is IOverseer` | `parent is IStateControllableMech sc && sc.ControllableByState`（IOverseer 繼承之） |
| `TryGetComp<CompDeadManSwitch>() is CompDeadManSwitch c && c.woken` | 基底快取 `pawn.DeadManSwitchComp?.woken == true` |
| `TryGetComp<CompCommandRelay>()` | `pawn.CommandRelayComp != null` |
| `TryGetComp<CompDrone>()` | `pawn.DroneComp != null` |
| `kindDef.race.HasComp(typeof(CompCommandRelay))` | 改為 instance 檢查（快取） |

> 設計要點：`IStateControllableMech` 是「結果層」的單一入口——Overseer（`IOverseer`）與 woken DMS 機械都走同一屬性，patch 不再需要理解各 comp 的內部狀態。AMO 預設 `true` 即為此介面的一種實作。

### 3.4 AMO 子類別

```csharp
// Mech\AMO\ArtificialOrganism.cs（新檔）
namespace Fortified;

/// <summary>
/// 軍用人工生物（AMO）基底：預設「狀態可控」、
/// 不可被機械師/監督者修理、自體再生、以口糧補能量。
/// 以 Pawn + IWeaponUsable 為基底（與 WeaponUsableMech / HumanlikeMech 同層）。
/// </summary>
public class ArtificialOrganism : Pawn, IWeaponUsable, IStateControllableMech
{
    public bool ControllableByState => true; // 預設 true，無需額外檢查

    // —— 快取 comps（§3.2 同模式）——
    private CompOverseerSubject cachedOverseerSubject;
    private CompDeadManSwitch cachedDeadManSwitch;
    private CompCommandRelay cachedCommandRelay;
    private CompDrone cachedDrone;
    private CompMechRepairable cachedMechRepairable;

    public CompOverseerSubject OverseerSubjectComp => cachedOverseerSubject ??= GetComp<CompOverseerSubject>();
    public CompDeadManSwitch DeadManSwitchComp => cachedDeadManSwitch ??= GetComp<CompDeadManSwitch>();
    public CompCommandRelay CommandRelayComp => cachedCommandRelay ??= GetComp<CompCommandRelay>();
    public CompDrone DroneComp => cachedDrone ??= GetComp<CompDrone>();
    public CompMechRepairable MechRepairableComp => cachedMechRepairable ??= GetComp<CompMechRepairable>();

    // —— IWeaponUsable 實作（與 WeaponUsableMech 相同簽名）——
    public void Equip(ThingWithComps equipment) { /* 同 WeaponUsableMech.Equip */ }
    public void Wear(ThingWithComps apparel)    { /* 同 WeaponUsableMech.Wear */ }
}

// Mech\AMO\ArtificialOrganismHumanlike.cs（新檔）
namespace Fortified;

/// <summary>
/// 人型 AMO（如 DMS 蛙人）：在 ArtificialOrganism 之上加入
/// HumanlikeMech 的人型初始化邏輯（story/style/skills/渲染/服裝），
/// 透過共用 helper（§3.4.2）取得，不重複 code。
/// </summary>
public class ArtificialOrganismHumanlike : ArtificialOrganism, IHumanlikeMech
{
    public HumanlikeMechExtension Extension => def.GetModExtension<HumanlikeMechExtension>();
    public Graphic HeadGraphic => HumanlikeMechUtility.GetHeadGraphic(this);

    public override void PostMake()    { base.PostMake(); HumanlikeMechUtility.CheckTracker(this); }
    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        HumanlikeMechUtility.CheckTracker(this);
    }
    public override void ExposeData()
    {
        base.ExposeData();
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            HumanlikeMechUtility.CheckTracker(this);
            Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }
}
```

> **C# 單一繼承限制（已定案：選項 A）**：`ArtificialOrganism : Pawn, IWeaponUsable` 無法同時繼承 `HumanlikeMech`（其本身已是 `Pawn, IWeaponUsable`）。**採用選項 A**——把 `HumanlikeMech` 的人型初始化邏輯（`CheckTracker`、`ApplyWorkTypeRestrictions`、渲染）抽取為共用 helper，`HumanlikeMech` 與 `ArtificialOrganismHumanlike` 都呼叫它。

### 3.4.1 `IHumanlikeMech` 標記介面（選項 A 的必要配套）

`ArtificialOrganismHumanlike` 不繼承 `HumanlikeMech` 後，現有 **11 處 `is HumanlikeMech` 檢查**（渲染樹、頭部圖形、服裝、gear tab、style、工作設定等）會全部漏掉它。新增標記介面，讓「人型機械」的判定改走介面：

```csharp
// Mech\HumanlikeMech\IHumanlikeMech.cs（新檔）
namespace Fortified;

/// <summary>
/// 人型機械標記：凡具備 HumanlikeMech 的人型初始化/渲染/服裝支援的
/// Pawn 皆實作此介面（HumanlikeMech、ArtificialOrganismHumanlike…）。
/// </summary>
public interface IHumanlikeMech
{
    HumanlikeMechExtension Extension { get; }
    Graphic HeadGraphic { get; }
}
```

- `HumanlikeMech : Pawn, IWeaponUsable, ICaravanOwner, IHumanlikeMech`（既有類別，追加介面，成員已存在）。
- `ArtificialOrganismHumanlike : ArtificialOrganism, IHumanlikeMech`（新增）。
- 改寫點（`is HumanlikeMech` → `is IHumanlikeMech`）：

| 檔案 | 行 | 用途 |
|---|---|---|
| `PawnRenderTree_SetupDynamicNodes_Patch.cs` | 19, 43 | 渲染樹動態節點/服裝節點 |
| `PawnRenderNode.cs` | 26 | 頭部 Graphic |
| `Patch_PawnRenderNodeWorker_Apparel_Head/Body_CanDrawNow.cs` | 11/12 | 服裝可繪製 |
| `Patch_Pawn_StyleTracker_CanDesireLookChange.cs` | 11 | 禁改髮型 |
| `ITab_Mech_Gear.cs` | 91 | gear tab 顯示 |
| `CompDrone.cs` | 217 | workSettings 初始化 |
| `MechApparelGenerator.cs` | 1261 | 服裝生成 |
| `Patch_Pawn_IsColonistPlayerControlled.cs` | 13 | 玩家可控 |
| `HumnalikeMechRenderingUtility.cs` | 19,44,63,110,169 | extension methods（未使用，改簽名即可） |

### 3.4.2 共用 helper（選項 A 的核心）

把 `HumanlikeMech` 的 `CheckTracker` / `ApplyWorkTypeRestrictions` 邏輯抽到靜態 helper，兩邊共用：

```csharp
// Mech\HumanlikeMech\HumanlikeMechUtility.cs（新檔）
namespace Fortified;

public static class HumanlikeMechUtility
{
    /// <summary>初始化人型機械的 story/style/skills/workSettings（原 HumanlikeMech.CheckTracker）。</summary>
    public static void CheckTracker(Pawn pawn);

    /// <summary>限制機械體工作類型（原 HumanlikeMech.ApplyWorkTypeRestrictions）。</summary>
    public static void ApplyWorkTypeRestrictions(Pawn pawn);

    /// <summary>計算頭部 Graphic（含髮型切換）。</summary>
    public static Graphic GetHeadGraphic(Pawn pawn);
}
```

- `HumanlikeMech` 的 `CheckTracker()` / `ApplyWorkTypeRestrictions()` / `HeadGraphic` 改為委派給 helper（或直接內聯 helper 內容，類別保留薄殼）。
- `ArtificialOrganismHumanlike` 覆寫 `PostMake` / `SpawnSetup` / `ExposeData` 呼叫 `HumanlikeMechUtility.CheckTracker(this)`，並實作 `IHumanlikeMech`。

### 3.5 不可修理＋自體再生

- **不可修理**：
  - 移除/不掛 `RimWorld.CompMechRepairable`（def 層）——`MechRepairUtility.CanRepair` 依賴該 comp（反編譯證據），沒有它 vanilla 修理與 `JobGiver_RepairMechs_Overseer` 的 `CanRepair(pawn, x)` 都會失敗。
  - `CompOverseer.Props.canRepair = false`（`CompProperties_Overseer.canRepair` 已存在，預設 `true`）——`JobGiver_RepairMechs_Overseer` 第一道檢查 `mech.Comp.Props.canRepair`（`Jobs.cs:17`）。
- **自體再生**：
  - 選項 A：在 AMO 的 def 掛 framework 現成的 self-heal hediff（`HediffComp_MechHeal`）——純 def 資料驅動，零新 code。
  - 選項 B：AMO 類別覆寫 `Tick`/`CompTick` 自行再生 `[UNVERIFIED 建議]`——若需要「再生需消耗口糧/能量」的耦合，則在 AMO 的專屬 comp 中實作（見 3.6）。

### 3.6 口糧→能量（兩個候選）

**候選 A：保留 vanilla `Need_MechEnergy`，由 AMO comp 自動消耗口糧充能（推薦）**

- 沿用現有 DMS `CompRationMagazine`（口糧艙）＋ `JobDriver_LoadRation`（裝填）。
- 新增自動消耗：AMO 專屬 `ThingComp.CompTick`（或 ThinkNode `JobGiver`，仿 `JobGiver_GetEnergy` 結構）：
  - 當 `needs.energy.CurLevel < 閾值` 且 `LoadedRations > 0` → 消耗 1 份，`CurLevel += 充能值`。
  - 閾值/充能值走 `CompProperties`（如 `consumeBelowLevel = 0.3f`、`energyPerRation`）。
- 優點：能量 UI、自關機、`JobGiver_GetEnergy` 電量下限（`Patch_JobGiver_GetEnergy_Max/Min` 的 `IOverseerMech.MinCharge/MaxCharge`）全部沿用；不用碰 `NeedDef`。
- 注意：`Need_MechEnergy` 的 `playerMechsOnly` 需要 `Faction == OfPlayer && OverseerSubject != null`——AMO 必須維持 `CompOverseerSubject`（現有 DMS 蛙人已有）或由 `Patch_CompOverseerSubject_State` 回傳 `Overseen` 使 energy need 成立。

**候選 B：自訂 NeedDef（needClass 繼承 `Need`，能量以口糧回升）**

- 需要自訂 `NeedDef`（`needClass` 指向 framework 新類別）、新 need 的下降/回升曲線、ITab/UI 整合，且 `JobGiver_GetEnergy` 系列不會自動處理它——工作量與相容性成本較高。
- 建議：僅在「口糧補能量的量無法對應 vanilla energy 語意」時採用。

**建議：候選 A**（沿用 `Need_MechEnergy`，口糧自動充能＋既有 `CompAbilityEffect_RationRecovery` 保留為主動技能）。

---

## 4. 分階段實作步驟

> **執行狀態**：Phase 1 ✅、Phase 2 ✅、Phase 3 ✅（Framework 側）、Phase 4 ✅（Framework 側）、Phase 5 ✅（Framework 側）、Phase 6 ⏳（需遊戲內驗證）

### Phase 1 — 介面與基底快取（框架核心）✅
1. 新增 `Mech\Overseer\IStateControllableMech.cs`（`ControllableByState`）。
2. 改 `Mech\Overseer\IOverseer.cs`：`IOverseer : IStateControllableMech`；`OverseerMech` / `HumanlikeOverseerMech` / `Building_Overseer` 實作 `ControllableByState => true`。
3. `WeaponUsableMech` / `HumanlikeMech` 加入快取 comps（§3.2），在 `SpawnSetup` 預先填值。
4. 編譯驗證：`_Tools\BuildDLL` 產出 `1.6\Assemblies\Fortified.dll`。
   - 補充：新增 `Mech\ICachedMechComps.cs` 共用介面（快取 comps 存取），`WeaponUsableMech` / `HumanlikeMech` / `ArtificialOrganism` 皆實作。

### Phase 2 — patch 改寫（消除 Comp 查詢）✅
5. 依 §3.3 表格逐一改寫：`Patch_CompOverseerSubject_State`、`Patch_InMechanitorCommandRange`、`Patch_MechanitorUtility_CanDraftMech`、`Patch_CanControlMechs`、`Patch_IsColonyMechPlayerControlled`、`Patch_Pawn_DraftController_ShowDraftGizmo`、`Patch_CanDropWeapon`、`Patch_MechInteracte`。
6. 同步改 `JobGiver_RepairSelf` / `ThinkNode_Condition_Wake`（`Mech\Awaken\Job\`）走快取；`JobGiver_RepairMechs_Overseer.CanRepair`、`CompDeadManSwitch.CompInspectStringExtra` 亦改。
7. 每改一個 patch 即用 rimsearcher 對照對應 def（如 `DMS_Mech_Frogman`、`DMS_Mech_FieldCommand`、`DMS_Mech_Sergeant`）確認行為等價。
   - 註：`Patch_MechanitorUtility_CanDraftMech` 保留 def 層 `race.HasComp(typeof(CompCommandRelay))` 作為 spawn 前語境 fallback（行為等價）。
   - 註：`Patch_CanDropWeapon` 中 `CompVehicleWeapon` 不在快取清單，保留 `TryGetComp`。

### Phase 3 — AMO 類別（ArtificialOrganism，選項 A）✅（Framework 側）
8. 新增 `Mech\AMO\ArtificialOrganism.cs`：`ArtificialOrganism : Pawn, IWeaponUsable, IStateControllableMech`，`ControllableByState => true`，含快取 comps 與 IWeaponUsable 實作。
9. 新增 `Mech\HumanlikeMech\IHumanlikeMech.cs` 標記介面（§3.4.1）與 `Mech\HumanlikeMech\HumanlikeMechUtility.cs` 共用 helper（§3.4.2）。
10. 重構 `HumanlikeMech`：委派 `CheckTracker`/`ApplyWorkTypeRestrictions`/`HeadGraphic` 給 helper，追加 `IHumanlikeMech`；把 13 處 `is HumanlikeMech` 改為 `is IHumanlikeMech`。
11. 新增 `Mech\AMO\ArtificialOrganismHumanlike.cs`：`ArtificialOrganismHumanlike : ArtificialOrganism, IHumanlikeMech`（§3.4 骨架）。
12. DMS 端：`DMS_Mech_Frogman.thingClass` 由 `Fortified.HumanlikeMech` 改為 `Fortified.ArtificialOrganismHumanlike`（`Races_Synthroid_Frogman.xml:14`）`[需與 DMS 作者協調；Framework 先提供類別]`。

### Phase 4 — 不可修理＋自體再生 ✅（Framework 側）
13. `ArtificialOrganism.Repairable => false`（virtual，可覆寫）；修理判定點擋 AMO：`JobGiver_RepairMechs_Overseer`（TryGiveJob + CanRepair）、`FloatMenuOptionProvider_OverseerMech`、`JobGiver_RepairSelf`。
14. 自體再生：選項 B 實作——新增 `CompArtificialOrganism`（regenIntervalTicks / regenHealAmount，隨時間移除非永久 `Hediff_Injury`）。
    - DMS 側（協調）：蛙人 def 移除 `RimWorld.CompMechRepairable`；Overseer 修理流程 `CompOverseer.Props.canRepair` 設 false（若 AMO 兼為 IOverseer）。

### Phase 5 — 口糧→能量 ✅（Framework 側）
15. 新增 `Mech\AMO\IRationSource.cs`（RationDef / LoadedRations / MaxRations / TryConsume）——Framework 不依賴 DMS 型別。
16. `CompArtificialOrganism` 內建口糧→能量：能量低於 `consumeBelowLevel` 且口糧來源有存糧時自動 `TryConsume` 充能（`energyPerRation`）。
    - DMS 側（協調）：`CompRationMagazine` 實作 `Fortified.IRationSource`。
17. 保留 `DMS_FrogmanRationRecovery` 主動能力（可選）。
    - 新增 Keyed：`FFF.ArtificialOrganismRations`（EN/繁中/簡中）。

### Phase 6 — 驗證
17. 遊戲內：woken 蛙人可被選取/下命令（`CanDraftMech`、`ShowDraftGizmo`、`InMechanitorCommandRange`、`IsColonyMechPlayerControlled`）。
18. 遊戲內：蛙人能量低於閾值時自動消耗口糧回升；不進 `JobGiver_GetEnergy_Charger`（不可充電）`[需確認 AMO 不掛 CompMechPowerCell]`。
19. 遊戲內：蛙人受傷後隨時間自體再生；機械師修理工作不對其生效。
20. 遊戲內：蛙人仍顯示人型渲染/服裝/gear tab（`IHumanlikeMech` 改寫後行為等價）。
21. rimsearcher：`get DMS_Mech_Frogman --brief` 確認 class 橋接更新；`values compClass` 確認無殘留舊類。

---

## 5. 相容性與風險

| 風險 | 說明 | 緩解 |
|---|---|---|
| 下游 DLL（DMS.dll 等）引用舊類別 | `ArtificialOrganism` / `ArtificialOrganismHumanlike` 為新增類別，`HumanlikeMech` 簽名不變，理論上二進位相容 | Phase 1 不改既有類別簽名；僅新增介面與屬性 |
| `ArtificialOrganismHumanlike` 無法繼承 `HumanlikeMech` | C# 單一繼承：人型邏輯透過共用 helper（§3.4.2）取得 | 已定案選項 A；`IHumanlikeMech` 標記介面讓渲染/服裝/UI 檢查不遺漏 |
| `is HumanlikeMech` 檢查遺漏 AMO | 11 處硬編碼型別檢查 | §3.4.1 全部改為 `is IHumanlikeMech` |
| default interface member 與舊編譯器 | 若 DMS 以 .NET Framework 4.x 舊 Roslyn 編譯，default member 可能出問題 | 避免 default member，改由各實作類別顯式實作 `ControllableByState` |
| `TryGetComp` 語意（null vs 存在） | 快取 null 與即時查詢等價 | 快取後不因 comp 動態增刪而失效——framework 中 comps 由 def 固定 |
| `Need_MechEnergy` 的 `playerMechsOnly` | AMO 若失去 `OverseerSubject` 就沒有 energy need | 維持 `CompOverseerSubject` + `Patch_CompOverseerSubject_State` 回傳 `Overseen` |
| 修理判定依賴 `CompMechRepairable` | 移除後 vanilla/自訂修理全失效 | 正是預期行為；若有其他模組對 AMO 修理需另行相容 |

---

## 6. 待確認事項

- [x] 人型邏輯抽取方式：**已定案選項 A**（`HumanlikeMechUtility` 共用 helper + `IHumanlikeMech` 標記介面）。
- [ ] `ArtificialOrganism` 基底要涵蓋哪些共用邏輯（快取 comps、口糧充能、再生鉤子）？「不可修理」是否也放基底類別（防呆）？
- [ ] 口糧自動充能的閾值/每份能量數值（需與 DMS 平衡）。
- [ ] 自體再生選 A（def 驅動 hediff）或 B（專屬 comp，可與口糧耦合）。
- [ ] `ArtificialOrganism` / `ArtificialOrganismHumanlike` 放 Framework 或 DMS？本方案採 Framework（供多模組重用），DMS 僅改 thingClass。
