# StructureAugment 使用手冊：不改佈局檔，把新內容加進既有結構

`Fortified.Structures.StructureAugmentDef` 讓你把預警設施、砲塔、補給箱等新內容，依「擺放規則」加進已經存在的結構佈局，
完全不用改 `FFF_StructureDef` / `StructureLayoutDef` 檔案，也不受佈局重新匯出、旋轉影響。

FFF 結構 GenStep（`GenStep_FFFStructureGen` 及其子類別，例如 `GenStep_FFFGarrisonedStructure`）生成主結構後，
會找出所有符合的增補並套用。關掉某個 GenStep 的增補：`<applyAugments>false</applyAugments>`。

## 1. 範例

```xml
<Fortified.Structures.StructureAugmentDef>
  <defName>MyMod_OutpostSecurity</defName>
  <targetTags><li>MyMod_Outpost</li></targetTags>   <!-- 結構標籤或 GenStep 的 useTag -->
  <factions><li>MyMod_Army</li></factions>           <!-- 選填：只套用在這些派系的站點 -->
  <entries>
    <li>
      <thing>DMS_Building_Camera</thing>               <!-- 牆面附掛物自動走 WallMount -->
      <exteriorOnly>true</exteriorOnly>
      <count>2~3</count>
      <countPerLevel>0.5</countPerLevel>
    </li>
    <li>
      <thing>Turret_MiniTurret</thing>
      <placement>Perimeter</placement>
      <minLevel>2</minLevel>
    </li>
  </entries>
</Fortified.Structures.StructureAugmentDef>
```

## 2. 欄位

**StructureAugmentDef**

| 欄位 | 說明 |
|---|---|
| `targetTags` | 結構的 `tags`，或 GenStep 的 `useTag`，任一相符即套用。 |
| `targetStructures` | 直接指定結構 defName。 |
| `factions` | 選填，只套用在這些派系的站點。 |
| `readiness` | `minLevel` / `countPerLevel` 讀哪個戒備（見 Readiness_Usage.md）；未填時用站點派系對應的戒備，沒有就當 0 級。 |
| `entries` | 增補清單。 |

**entries 每一項**

| 欄位 | 預設 | 說明 |
|---|---|---|
| `thing` / `stuff` | 必填 / 預設材料 | 要放的物件。 |
| `placement` | Perimeter | 擺放規則，見下。牆面附掛物（`isAttachment`）一律 WallMount。 |
| `facing` | Outward | 物件「作用的方向」；`None` 隨機。警報掃描器的 `scanRotationOffset` 會自動換算成 Rotation。 |
| `count` / `countByPoints` | 1 / 無 | 數量；`countByPoints` 依站點威脅點數，夾在 count 內。 |
| `countPerLevel` | 0 | 每一級戒備多放幾個（小數隨機進位）。 |
| `minLevel` | 0 | 戒備達到此級才放。 |
| `chance` | 1 | 整項出現機率。 |
| `minSpacing` | 6 | 與其他增補物件的最小間距。 |
| `exteriorOnly` | false | WallMount 只用外牆（面前是室外）。 |
| `outskirtsRange` | 14 | Outskirts 的最遠距離。 |
| `connectPower` / `powerSearchRadius` / `powerSource` | true / 30 / 無 | 用電物件沿最短路徑鋪電線接到同派系電網；找不到時在旁邊放 `powerSource`。 |

## 3. 擺放規則

| 規則 | 位置 | 朝向（作用方向） |
|---|---|---|
| `Perimeter` | 足跡外 1~2 格的一圈，露天 | 背向結構中心 |
| `Entrance` | 門的室外側、門洞左右兩旁（不擋門） | 背向門 |
| `Indoor` | 足跡內有屋頂、不貼門的地板 | 背向中心（或隨機） |
| `WallMount` | 貼著牆的格子，面前至少 3 格可通行 | 背向牆 |
| `Outskirts` | 足跡外 3~outskirtsRange 格的露天空地 | 背向結構中心 |

所有規則都只用空格子：沒有建築、非水面、地形承重足夠、沒有 pawn；多格物件的每一格都要符合。

## 4. 為什麼不用其他做法

| 做法 | 問題 |
|---|---|
| 逐一改佈局檔 | 量大，重新匯出佈局就沒了 |
| XML Patch 往 `elements` 加座標 | 座標寫死，佈局一改或旋轉就錯位 |
| 子結構槽位（`FFF_Element_RandomSubStructure`） | 適合房間大小的模組，但每個佈局都得先加槽位；可與增補並用 |
