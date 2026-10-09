# Huntdown 使用手冊：派系追緝

追緝（Huntdown）讓某個派系持續追蹤玩家：先送出預警信件，再對被追緝的地圖發動數波襲擊。
它是原版 Odyssey `ScenPart_PursuingMechanoids` 的通用版，參數全部移到 `Fortified.HuntdownDef`，
執行狀態放在 `GameComponent_Huntdown`，所有操作都透過 `Fortified.HuntdownUtility`。

| 類別 | 用途 |
|---|---|
| `HuntdownDef` | 派系、時程、波次、追蹤條件、文字 |
| `HuntdownWorker` | 可繼承的行為：能否追蹤地圖、是否仍追緝、預警信件、發動襲擊 |
| `HuntdownUtility` | 啟動、結束、查詢、延後、立即發動 |
| `GameComponent_Huntdown` | 存檔與計時（每 250 tick 檢查一次） |
| `ScenPart_Huntdown` | 開局即啟動 |
| `Alert_FFF_Huntdown` | 右側倒數警報，自動出現，不需註冊 |

## 1. 定義一個追緝

```xml
<Fortified.HuntdownDef>
  <defName>MyMod_FleetHuntdown</defName>
  <label>fleet huntdown</label>
  <faction>MyMod_Fleet</faction>
  <goodwillChangeOnStart>-200</goodwillChangeOnStart>
  <requireGravEngine>true</requireGravEngine>
  <letterLookTargets>
    <li>PilotConsole</li>
  </letterLookTargets>
  <letterLabel>Hunted</letterLabel>
  <letterText>{0} need you dead.</letterText>
</Fortified.HuntdownDef>
```

文字欄位裡的 `{0}` 會換成派系名稱；留空時使用 FFF 的預設文字（`Keyed/Huntdown.xml`）。

## 2. 全部欄位

| 欄位 | 預設 | 說明 |
|---|---|---|
| `faction` | 必填 | 追緝方派系。 |
| `workerClass` | `HuntdownWorker` | 自訂行為時換成子類別。 |
| `initialWarningDelay` / `initialRaidDelay` | 2700 / 30000 | 以 initial 追蹤的地圖（開局地圖）使用。 |
| `warningDelayRange` / `raidDelayRange` | 840000~960000 / 1080000~2100000 | 之後追蹤的地圖使用。 |
| `waves` | 兩波：0 tick ×1.5（下限 2000）、30000 tick ×2（下限 8000） | `delayTicks` 從首波起算；`pointsMultiplier` 乘上地圖威脅點數；`minPoints` 為下限。 |
| `repeatWhileStaying` | false | 最後一波結束後若仍在該地圖，用隨機延遲重新排程。 |
| `criticalAlertLeadTicks` | 60000 | 首波前多久警報轉紅。 |
| `raidArrivalMode` / `raidStrategy` | 無 | 襲擊方式；未填時交給 RaidEnemy 依說書人規則挑選。 |
| `postponeWhileNotHostile` | true | 派系不敵對（例如軍事法庭休戰）時延後這一波並重新排程，避免 RaidEnemy 換成隨機敵對派系。 |
| `questsOnWave` | 無 | 每波後提供的任務：`quest`、`chance`（預設 1）、`siteThreatPoints`。同一任務待接或進行中時不重複。 |
| `followGravship` | true | 玩家重力船降落時，追緝跟到新地圖。 |
| `retargetPlayerHome` | false | 沒有被追緝的地圖時自動改追任一玩家據點（追殺整個殖民地）。 |
| `stopWhenBoundPawnLost` | true | 綁定角色死亡或消失時結束追緝。 |
| `requireGravEngine` | false | 地圖上沒有玩家重力引擎時停止追緝該地圖（原版行為）。 |
| `requiredThings` | 無 | 地圖上必須有其中之一，否則停止追緝該地圖（例如被追蹤的石碑）。 |
| `excludedMapGenerators` | Mechhive | 不追緝的地圖類型。 |
| `goodwillChangeOnStart` | 0 | 啟動時對玩家的好感度變化。 |
| `letterLookTargets` | 無 | 預警信件指向的物件，找不到時改指 `requiredThings`，再找不到就指向地圖。 |
| `sendWarningLetter` / `showAlert` | true / true | 是否送預警信件、顯示倒數警報。 |
| `letterLabel` … `alertExplanationCritical` | Keyed 預設 | 信件與警報文字。 |

## 3. 開局即被追緝

```xml
<li Class="Fortified.ScenPart_Huntdown">
  <def>FFF_Huntdown</def>
  <huntdown>MyMod_FleetHuntdown</huntdown>
</li>
```

`bindStartingPawn` 設為 true 時綁定第一名開局角色（見第 5 節）。

需要禁止移除某派系時，自己定義 ScenPartDef，`scenPartClass` 填 `Fortified.ScenPart_Huntdown`，並加上 `preventRemovalOfFaction`。

## 4. 從程式碼調用

```csharp
// 啟動並立即追緝目前地圖（initial = 用較短的開局延遲）
HuntdownUtility.Start(MyDefOf.MyMod_FleetHuntdown, map, initial: false, source: "Quest_123");

// 只啟動、不排程：之後玩家重力船降落時才開始追（followGravship）
HuntdownUtility.Start(def);

// 結束（例如任務完成、被追蹤的物件被摧毀）
HuntdownUtility.Stop(def);

// 干擾：把這張地圖的預警與襲擊延後一天
HuntdownUtility.Delay(def, map, GenDate.TicksPerDay);

// 查詢
bool hunted = HuntdownUtility.IsMapHunted(map);
int ticks = HuntdownUtility.TicksUntilNextWave(def, map);   // 無排程時為 -1
```

其他：`TrackMap` / `UntrackMap` 手動增減追蹤地圖，`FireNextWaveNow` 立即發動下一波，`AllActive` 列出所有進行中的追緝。

## 5. 綁定角色與暫停

```csharp
// 綁定被追緝的角色；他死亡時結束（stopWhenBoundPawnLost），也可由任務主動結束
HuntdownUtility.Start(def, map, boundPawn: pawn);
HuntdownUtility.StopAllBoundTo(defendant);          // 例如軍事法庭審判了這個人

// 暫停（例如摧毀追蹤網路節點）：已在暫停中會疊加；結束時所有地圖重新排程並呼叫 OnResumed
HuntdownUtility.Suspend(def, GenDate.TicksPerYear);
HuntdownUtility.IsSuspended(def);
HuntdownUtility.SuspendedTicksLeft(def);
HuntdownUtility.ResumeNow(def);
```

## 6. 自訂行為

```csharp
public class HuntdownWorker_Stele : HuntdownWorker
{
    // 石碑被搬上船或被摧毀後就不再追這張地圖
    public override bool StillHunted(Map map) => base.StillHunted(map) && map.listerThings.AnyThingWithDef(MyDefOf.MyStele);
}
```

可覆寫：`Faction`、`CanTrackMap`、`StillHunted`、`SendWarningLetter`、`TryFireWave`、`CanFireWave`、`OnWaveFired`、
`BoundPawnLost`、`OnStarted`、`OnStopped`、`OnSuspended`、`OnResumed`。

## 7. 除錯

開發者模式 → Debug actions → Fortified：

- Huntdown: start on current map
- Huntdown: fire next wave
- Huntdown: stop
- Huntdown: suspend 1 day / resume
- Huntdown: log status
