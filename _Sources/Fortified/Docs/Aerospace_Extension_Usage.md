# 航太框架擴充指南（防空、飛彈）

適用：RimWorld 1.7，不需要 Odyssey。程式在 `Fortified.dll`（namespace `Fortified`，只編進 1.7），Def 在 `1.7/Missiles/`。

範例 mod：`_Sources/Examples/AerospaceSample/`（不隨 FFF 發布；複製到 `Mods/` 就能載入，原始碼在 `Source/`）。

## 開啟功能

航太功能平常不載入（Harmony 補丁、地圖與世界元件都不建立）。用到時任選一種方式開啟：

- 自己的 Def 用到框架型別（例如 `Class="Fortified.CompProperties_SearchRadar"`），FFF 會自動偵測。
- 明確宣告（建議；設定頁的功能清單會列出你的 mod）：

```xml
<Fortified.FFF_FeatureRequestDef>
  <defName>MyMod_Features</defName>
  <features><li>FFF_Feature_Missiles</li></features>
</Fortified.FFF_FeatureRequestDef>
```

功能開關的細節見 [FeatureSwitch_Usage.md](FeatureSwitch_Usage.md)。

## 擴充點

都不需要 Harmony。

| 想做的事 | 用什麼 | 範例 |
|---|---|---|
| 新感測器（紅外線、被動、聲學） | Comp 實作 `IAirSensor`，生成時 `MapComponent_AirEffectors.Register` | `CompInfraredSensor` |
| 新防空武器，世界層（雷射、電磁砲） | Comp 實作 `IAirEffector`，同樣向 `MapComponent_AirEffectors` 註冊 | `CompLaserPointDefense` |
| 新防空武器，地圖層（打落到這張地圖的空投與導彈） | Comp 實作 `IMapAirEffector`，`RegisterMapEffector` | `CompLaserPointDefense` |
| 讓防空追蹤新的飛行物 | 世界物件實作 `IAirTrack`；改不了的物件用 `AirTrackProvider` 包裝 | `WorldObject_ReconAircraft` |
| 新導引頭 | 繼承 `GuidanceType`，在 `MissilePartDef.guidanceType` 用 `Class=` 指定 | `GuidanceType_PowerPlant` |
| 新飛行行為 | 繼承 `MissileBehaviorProps`／`MissileBehavior`，寫在彈體或部件的 `behaviors` | — |
| 修改偵測、鎖定、命中率、誘餌識別 | `AirDefenseModifier`（或純 XML 的 `AirDefenseModifier_Factor`），寫在 `AirDefenseExtensionDef.modifiers` | `AirDefenseModifier_Weather` |
| 監聽防空事件 | `AirCombatListener`（XML）或 `AirCombatEvents.Raised`（C#） | `AirCombatListener_DevLog` |
| 在鎖定或開火之前擋下 | `AirCombatVeto`，寫在 `AirDefenseExtensionDef.vetoes`（或 `AirCombatEvents.AddVeto`） | `AirCombatVeto_MinHitChance` |
| 陣營的防空與打擊準則 | `MissileDoctrineDef`，用 `MissileDoctrineExtension` 掛到 `FactionDef` | `AerospaceSample_AggressiveDoctrine` |
| 導彈陣地選目標 | 繼承 `StrikeTargetWorker`，寫在準則的 `targetWorker` | — |
| 新的部件槽、尺寸分級、禁射類別、交戰領域 | XML 加 `MissilePartCategoryDef`、`MissileSizeClassDef`、`HoldFireClassDef`、`CombatDomainDef`（見下） | — |
| 封鎖圈攔截原版以外的載具 | `BlockadeHitUtility.RegisterTarget(IBlockadeTargetHandler)`：只要說明「是否在飛」與「被打中的效果」，接戰走一般流程 | — |

### 感測器 `IAirSensor`

- `Source`：所在的 Thing，陣營由它決定。
- `Active`：能否運作。可以直接用 `CompAirDefenseDevice.IsOperational(parent)`（電力、損壞、開關、功率段位、擊暈、操作員），與出貨的雷達同一套規則。
- `WorldDetectionRange(target)`：世界層探測半徑（地表格）。節點取所有感測器的最大值，再套干擾、警戒階段和修正鏈。
- `MapWarningTicks(target)`：地圖層預警窗口（ticks）。所有感測器相加，再乘目標的距離等級、高度係數，除以目標數。回傳 0 表示只參與世界層。

註冊後，同陣營在這張地圖就有防空節點；沒有感測器的地圖上，武器看不到目標。

### 武器 `IAirEffector`

- `UsesFireControl = true`：走完整流程（需要火控雷達、鎖定、佔火力通道，受火控射程與高度限制）。
- `UsesFireControl = false`：點防禦式。不鎖定、不佔通道；陣營看得到航跡且政策允許時，每次掃描都會詢問。
- `PrepareShot` 回傳的 `AngularSpeed <= 0` 表示瞬間命中，當場擲骰；大於 0 會產生攔截物飛過去。
- `CanEngage`、`PrepareShot` 不可有副作用；扣彈藥與冷卻放在 `TryFire`。
- 要限制交戰領域（空中、海面）時再實作 `IDomainAwareEffector`。

### 地圖層武器 `IMapAirEffector`

地圖層是來襲物（空投艙、導彈）已經落向這張地圖、被防禦方的感測器收進「波次」之後的階段。分兩段：

- **中過程**（`MapAirPhase.Midcourse`）：還在末端窗口外。出貨裝置是火控雷達＋攔截彈發射架。
- **末端**（`MapAirPhase.Terminal`）：進入末端窗口、只走末端（世界層已結算的導彈），或在目標上空盤旋。末端窗口來自防禦方能接戰的 CIWS；沒有 CIWS 時窗口是 0，整段都算中過程。

`IMapAirEffector` 是點防禦式、瞬間命中的武器：

- 不需要火控雷達、不鎖定、不佔火力通道。每 tick 每具武器最多開火一次，框架挑最早落地、`CanEngage` 回傳 true 的目標。
- `HitChance` 是基礎命中率；框架再套修正鏈，末端再乘目標的末端攔截倍率（彈道導彈很難在末端擊落），然後擲骰。命中時清掉目標並播放攔截特效，發出 `InterceptorLaunched`、`InterceptResolved` 事件。
- `TryFire` 負責冷卻、彈藥與射擊效果；回傳 false 時框架不擲骰。
- 中過程跳過已有攔截彈在飛的目標與已識破的誘餌；末端和 CIWS 一樣不識別誘餌。戰情終端的自動接戰開關與分類禁射同樣適用。
- 波次要有人建立：同陣營在這張地圖要有感測器（搜索雷達或 `IAirSensor`）。世界層已結算、只走末端的導彈例外：有能接戰的 CIWS 或註冊的 `IMapAirEffector` 就會進入末端波次。
- 有飛行時間的攔截彈不走這個介面，請用出貨的發射架。

一個 Comp 同時實作 `IAirEffector`、`IMapAirEffector` 或 `IAirSensor` 時，直接呼叫 `Register(this)` 會因多載而不明確，改用 `RegisterAll(this)`／`DeregisterAll(this)`。

### 否決 `AirCombatVeto`

事件處理者只看得到已經發生的事。要在動作**之前**擋下，用否決者：`Allows(AirCombatEvent e)` 回傳 false 就不做。

- 詢問 `LockStarted` 的地方：世界層與地圖層中過程開始火控鎖定。被否決時不鎖定、不佔通道，下次掃描再問。
- 詢問 `InterceptorLaunched` 的地方：世界層攔截彈與點防禦、地圖層發射架、肩射、CIWS 每輪連射開始、`IMapAirEffector`。被否決時不扣彈藥、不進冷卻；世界層與地圖層中過程的鎖定會放掉（釋放通道）。
- 查詢事件的 `Pending` 為 true；`HitChance` 是預計的命中率（已套修正鏈），鎖定時為 0。一般處理者不會收到查詢事件。
- 不可有副作用：同一個動作可能每次掃描都被問一次。丟例外時記錄一次錯誤並當成允許。
- 只想調整機率時用修正（`AirDefenseModifier`）；否決是「這次完全不做」。

### 航跡 `IAirTrack`

- 世界物件實作它後，加入世界時自動通知防空網路；偵測、鎖定狀態由 FFF 依物件 ID 存檔，物件本身不必存。
- `PositionAt`、`AltitudeAt` 會被攔截解反覆呼叫，不可有副作用。
- 被打中時呼叫 `Notify_Intercepted`，要銷毀或只承受傷害由航跡決定。
- **抵達時**（移除物件之前）呼叫 `WorldComponent_AirDefenseNetwork.ReportArrival(this)`：發出 `TrackArrived` 事件並通報 NPC 防空警戒。飛彈和空投幽靈航跡自己會處理。
- 想被識別成誘餌就再實作 `IDecoyTrack`。
- 想讓戰情終端的分類禁射管到它，覆寫 `HoldFireClass` 回傳一個 `HoldFireClassDef`。

## 分類 Def

原本是列舉的四種分類改成 Def，出貨的 defName 與舊列舉名稱相同，既有 XML（`<category>Guidance</category>`、`<sizeClass>Ballistic</sizeClass>`、`<domains><li>Underwater</li></domains>`）不必改。定義在 FFF `1.7/Missiles/Defs/Missile/FFF_MissileClasses.xml`。

| Def | 出貨的值 | 新增後的效果 |
|---|---|---|
| `MissilePartCategoryDef` | `Warhead`、`Guidance`、`Propulsion`、`Payload` | 彈體 `slots` 列出它就多一個槽；部件的數值修正照常加總。出貨四類的專屬行為（戰鬥部效果、導引頭、射程、特殊酬載）不會套到新類別。`order` 決定介面與型號命名的順序，`designKeyword` 是命名規則裡的符號（預設 defName 小寫）。 |
| `MissileSizeClassDef` | `Loitering`、`Light`、`Medium`、`Ballistic` | 發射架與儲藏架用它判斷能放什麼。原本寫死的差異改成欄位：`allowsLoiter`（能裝盤旋部件）、`groundChannelCost`（打擊規劃佔的地面通道）、`holdFireClass`（戰情終端的禁射分類）。彈體沒寫時是 `Medium`。 |
| `HoldFireClassDef` | `Loitering`、`DropPod`、`Ballistic` | 戰情終端多一列「攔截／不攔截」開關。飛彈看彈體分級的 `holdFireClass`，外部航跡實作 `IAirTrack.HoldFireClass`。 |
| `CombatDomainDef` | `Air`、`Underwater` | 火控雷達與 CIWS 的 `domains`、彈體與 `AirTargetExtension` 的 `domain` 都可以填新領域。欄位沒寫（null）一律當成 `Air`；比較前用 `CombatDomainUtility.Resolve`。 |

C# 用 `MissilePartCategoryDefOf`、`MissileSizeClassDefOf`、`HoldFireClassDefOf`、`CombatDomainDefOf` 取出貨的值，以參照比較（`==`），不能再用 `switch`。

## 存檔相容

改類別名稱或 defName 時，在自己的 mod 加一份 `Fortified.DefRenameDef`：`renames` 對照 defName，`typeRenames` 對照類別（舊名寫完整名稱，新名寫型別）。範例見 FFF `1.7/Missiles/Defs/Missile/FFF_MissileRenames.xml`。

## 還不支援

- **Multiplayer**：沒有做同步。玩家操作（Gizmo、戰情終端、打擊規劃、裝配台）沒有註冊成同步動作，聯機會不同步；Multiplayer 啟用時 FFF 會在 Log 與航太設定頁提醒。模擬本身已盡量確定：裝置註冊表依 thingIDNumber 排序（`OrderedCompSet`），特效亂數用獨立狀態，會落地的主殘骸數量不受顯示設定影響。自己的擴充請照同樣規則：遊戲狀態不要依賴 `HashSet` 走訪順序、玩家看哪張地圖、即時時間或每位玩家各自的設定；只影響畫面的亂數用 `Rand.PushState`／`PopState` 包起來。
- **地圖層武器**：第三方只能加瞬間命中的點防禦（`IMapAirEffector`）。出貨的攔截彈發射架、肩射與 CIWS 仍各走自己的流程，沒有改成這個介面。
- **封鎖攔截**：已併入一般接戰（封鎖據點的所有武器都會用，含點防禦式）。但封鎖只針對 `BlockadeHitUtility.InFlight` 認得的載具（運輸艙、重力船、註冊的 `IBlockadeTargetHandler`），一般 `IAirTrack` 照原本的規則由各節點接戰，不另外受封鎖圈限制。
- **CE**：Combat Extended 啟用時由 `FortifiedCE.dll` 接手爆炸與砲兵；`IAirEffector`、`IAirSensor` 不受影響。
