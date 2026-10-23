# Readiness 使用手冊：派系戒備（DEFCON）與連隊

戒備（Readiness）讓一個派系有「警戒等級」：玩家的挑釁讓它升級，平靜時隨時間降級；每一級有自己的行動表，
會發任務、部署連隊、圍攻與接管聚落、直接襲擊殖民地，最高級還能對其他派系宣戰並啟動追緝（Huntdown）。
原本是古代軍團（DMS AncientCorps）寫死的 DEFCON，現在全部由 XML 定義。

| 類別 | 用途 |
|---|---|
| `ReadinessDef` | 派系、各級設定、宣戰、追緝、文字 |
| `ReadinessLevel` | 一級：行動間隔、降級時間、節奏、連隊上限、行動表 |
| `ReadinessAction_*` | 行動表裡的動作（可自訂子類別） |
| `ReadinessWorker` | 可繼承的行為：信件、宣戰對象、接管、巡邏反應 |
| `ReadinessUtility` | 升級、降級、累積壓力、查詢 |
| `GameComponent_Readiness` | 存檔與計時（每 250 tick） |
| `Alert_FFF_Readiness` | 右側提示：等級、情勢、降級倒數、戰爭目標、壓力 |
| `CompanyDef` / `PlatoonDef` | 連隊與排的編制 |
| `FFF_Company` / `SitePartWorker_FFF_Company` | 世界地圖上的連隊與其駐地 |
| `WorldObjectCompProperties_ReadinessPatrol` | 站點的巡邏增援，隨等級加快、加強 |
| `CompProperties_UseEffect_RaiseReadiness` | 使用物品／建築時升級或累積壓力 |

## 1. 最小設定

```xml
<Fortified.ReadinessDef>
  <defName>MyMod_Alert</defName>
  <faction>MyMod_Army</faction>
  <levels>
    <li>
      <label>Peacetime</label>
      <decays>false</decays>
    </li>
    <li>
      <label>Alert</label>
      <description>{0} has doubled its patrols.</description>
      <decayDays>5~10</decayDays>
      <actionIntervalDays>3~6</actionIntervalDays>
      <tempo>0.75</tempo>
      <actions>
        <li Class="Fortified.ReadinessAction_GiveQuest">
          <quest>MyMod_OutpostQuest</quest>
          <chance>0.5</chance>
        </li>
      </actions>
    </li>
  </levels>
</Fortified.ReadinessDef>
```

升級：`ReadinessUtility.Raise(def)`；或在物品上掛 `CompProperties_UseEffect_RaiseReadiness`。

## 2. ReadinessDef 欄位

| 欄位 | 預設 | 說明 |
|---|---|---|
| `faction` | 必填 | 戒備所屬派系。`ReadinessUtility.DefFor(faction)` 用它反查。 |
| `workerClass` | `ReadinessWorker` | 自訂行為時換成子類別。 |
| `levels` | 必填 | 索引即等級，0 最低。 |
| `startingLevel` | 0 | 新遊戲的等級。 |
| `pressurePerLevel` | 1 | `AddPressure` 累積滿這個值升一級。 |
| `dropClearsPressure` | true | 降級時清空壓力。 |
| `companyWorldObject` | 無 | 部署連隊用的 WorldObjectDef（類別須為 `FFF_Company` 或子類別）；不填就不部署連隊。 |
| `warMinLevel` | -1 | 達到此等級時向科技最高的敵對人形派系宣戰；-1 不宣戰。 |
| `levelAfterWar` / `warOverMinLevel` | 2 / 2 | 目標覆滅後降到的等級；只有高於 warOverMinLevel 才發信並降級。 |
| `warTargetsHumanlikeOnly` | true | 只對人形派系宣戰。 |
| `takeoverLetterChance` | 0.25 | 接管聚落時發信的機率。 |
| `huntdown` / `huntdownMinLevel` | 無 | 達到該等級啟動追緝、低於時停止。 |
| `showAlert` / `alertMinLevel` | true / 1 | 右側提示。 |
| `sendLevelLetters` | true | 升降級信件。 |
| 文字欄位 | FFF 預設 | `riseLetterLabel/Text`、`dropLetterLabel/Text`、`alertLabel/Explanation`、`warLetterLabel/Text`、`warOverLetterLabel/Text`、`takeoverLetterLabel/Text`。 |

**文字欄位可以直接寫字，也可以寫 Keyed 鍵**——能翻譯就翻譯，所以既有的 Keyed 翻譯不必搬到 DefInjected。
升降級：`{0}` 派系、`{1}` 等級標籤；信件會在後面接上該級的 `description`。宣戰／結束：`{0}` 本派系、`{1}` 目標。
接管：`{0}` 本派系、`{1}` 原派系、`{2}` 舊名、`{3}` 新名。

## 3. ReadinessLevel 欄位

| 欄位 | 預設 | 說明 |
|---|---|---|
| `label` / `description` | 「等級 N」/ 無 | 可寫 Keyed 鍵；description 的 `{0}` 為派系。 |
| `decayDays` / `decays` | 5~10 / true | 沒有新挑釁時多久降一級。0 級的 decays 不影響（不會再降）。 |
| `actionIntervalDays` | 無 | 行動間隔；不填表示此等級不行動。 |
| `tempo` | 1 | 巡邏節奏倍率，越小越頻繁、巡邏人數抖動越大。 |
| `maxCompanies` | 0 | 連隊上限：和平時多出來的會撤回（閒置的先撤），戰時補滿。 |
| `riseLetterDef` | 自動 | 升到此級的信件類型；未填時低級為負面事件、中級小威脅、最高級大威脅。 |
| `actions` | 無 | 行動表，見下。 |

## 4. 行動表

每次行動先調整連隊數量，再依序跑 `actions`。每個動作各自擲 `chance`；`exclusive` 的動作成功就結束這次行動；
任何讓等級改變的動作也會結束（新等級的行動表從下一次開始）。共用欄位：

| 欄位 | 說明 |
|---|---|
| `chance` | 執行機率（預設 1）。 |
| `exclusive` | 成功後結束這次行動。 |
| `requireWar` | 只在戰爭中執行。 |
| `requireHostileToPlayer` | 只在與玩家敵對時執行。 |

| 動作 | 說明 |
|---|---|
| `ReadinessAction_RaiseLevel` / `_DropLevel` | 自行升／降 `amount` 級。 |
| `ReadinessAction_GiveQuest` | 以玩家據點威脅點數 × `pointsFactor` 提供 `quest`；`skipIfActive` 時同一任務待接或進行中不重複。 |
| `ReadinessAction_CompanyOrders` | 對隨機連隊下 `count + perCompany × 連隊數` 道指令。 |
| `ReadinessAction_DeployCompanies` | 部署 `count` 個連隊（戰爭目標優先）；`respectCap` 時不超過上限。 |
| `ReadinessAction_DisbandCompanies` | 解散 `count` 個連隊，-1 全部。 |
| `ReadinessAction_CompanyRaid` | 派一個連隊直接襲擊玩家據點（預設需敵對）。 |
| `ReadinessAction_Takeover` | 隨機接管一個敵對非玩家聚落，人形派系優先。 |
| `ReadinessAction_Incident` | 以本派系對玩家據點觸發 `incident`（點數 × `pointsFactor`）。 |

自訂動作：繼承 `ReadinessAction`，實作 `Run(def, state, faction)`，回傳是否真的做了事。

## 5. 連隊

```xml
<Fortified.CompanyDef>
  <defName>MyMod_ArmorCompany</defName>
  <label>armour company</label>
  <defaultFaction>MyMod_Army</defaultFaction>
  <minLevel>2</minLevel>              <!-- 戒備達到此級才部署 -->
  <NamePack>MyMod_CompanyNames</NamePack>
  <IconPath>UI/Icons/MyCompany</IconPath>
  <combatRadius>10</combatRadius>
  <squadCountRange>2~3</squadCountRange>
  <squads>
    <li>MyMod_TankPlatoon</li>
  </squads>
</Fortified.CompanyDef>

<Fortified.PlatoonDef>
  <defName>MyMod_TankPlatoon</defName>
  <leaderKindDef><li>MyMod_Commander</li></leaderKindDef>
  <fixedPawnkind><MyMod_Tank>2</MyMod_Tank></fixedPawnkind>   <!-- 數量 -->
  <memberCountRange>3~5</memberCountRange>
  <memberKindDefs><li>MyMod_Infantry</li></memberKindDefs>
</Fortified.PlatoonDef>
```

指令（每次依序擲骰）：有目標時 `holdChance` 按兵不動 → `takeoverChance` 接管 → `splitChance` 分兵（另派連隊接手，自己轉向）→
`razeChance` 夷平；沒目標時 `idleDisbandChance` 撤回，否則戰時領取下一個戰爭目標、和平時遊走。
駐地每 `raidCooldownDays` 會對作戰半徑內的玩家據點擲一次襲擊。玩家攻入駐地時整個連隊就是守軍；
領隊全滅後，玩家離開時駐地也會撤走。襲擊信件 `raidLetterLabel/Text`（`{0}` 派系、`{1}` 連隊名）。

連隊的 WorldObjectDef：

```xml
<WorldObjectDef>
  <defName>MyMod_CompanyCamp</defName>
  <worldObjectClass>Fortified.FFF_Company</worldObjectClass>
  <canHaveFaction>true</canHaveFaction>
  <comps>
    <li Class="WorldObjectCompProperties_Abandon" />
    <li Class="WorldObjectCompProperties_FormCaravan" />
  </comps>
  <modExtensions>
    <li Class="Fortified.ModExtension_CompanyWorldObject">
      <sitePart>MyMod_CompanySite</sitePart>   <!-- workerClass 為 Fortified.SitePartWorker_FFF_Company -->
    </li>
  </modExtensions>
</WorldObjectDef>
```

自訂 AI：繼承 `CompanyWorker`（`MakeRaidLord`、`MakeGarrisonLord`、`HasCommander`、`SendRaidLetter`），在 `workerClass` 指定。

## 6. 巡邏增援

```xml
<li Class="Fortified.WorldObjectCompProperties_ReadinessPatrol">
  <MBTTime>30000</MBTTime>
  <averagePawnCount>5</averagePawnCount>
  <patrolCountToRaiseLevel>3</patrolCountToRaiseLevel>
  <possibleGeneratePawn><MyMod_Infantry>1</MyMod_Infantry></possibleGeneratePawn>
  <tiers>
    <li><minLevel>2</minLevel><count>2</count><options><MyMod_Tank>1</MyMod_Tank></options></li>
  </tiers>
</li>
```

玩家在站點地圖時，巡邏隊會從邊緣進入，間隔一波比一波短並乘上等級 `tempo`。第一支交給
`ReadinessWorker.OnPatrolArrived(first: true)`（預設不做事，子類別可在此宣告敵對）；之後每支累積
`1 / patrolCountToRaiseLevel` 的壓力並提前下一次行動。`tiers` 的數量為 `count / tempo` 進位。
舊欄位 `possibleGeneratePawn_Medium` / `_Heavy` 仍可用（等同 minLevel 1 / 3 的 tier）。

## 7. 程式介面

```csharp
ReadinessUtility.Raise(def);                  // 升一級（最高級時只重置降級計時）
ReadinessUtility.Drop(def);
ReadinessUtility.SetLevel(def, 3);
ReadinessUtility.AddPressure(def, 0.25f);     // 小事件累積
ReadinessUtility.Hasten(def);                 // 提前下一次行動
ReadinessUtility.LevelOf(def) / LevelOf(faction) / TempoOf(def) / WarTargetOf(def);
CompanyUtility.RaidMap(map, companyDef, faction);
```

除錯選單 `Fortified` 分類：升／降級、立即行動、加壓力、部署連隊、命令所有連隊、連隊襲擊、在此生成巡邏、輸出狀態。
