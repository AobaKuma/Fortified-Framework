# FactionRelationUtility 使用手冊：設定確切的派系關係

`Fortified.FactionRelationUtility` 把派系關係「設定為」確切的好感度與關係類型，而不是「調整」。
劇情上的確定結果都應該走這裡：休戰、宣判、制裁、開局指定好感度、任務獎懲中的「重置」。

一般的好感度增減（送禮、懲罰、外交事件）仍然用原版的 `Faction.TryAffectGoodwillWith`。

## 1. 為什麼不用原版

原版沒有「把好感度設成某個值」的 API，常見寫法是 `TryAffectGoodwillWith(target - current)`。它有三個問題：

| 問題 | 原因 | 結果 |
|---|---|---|
| 會溢出 | `CalculateAdjustedGoodwillChange` 往自然好感度多推最多 25% | 從 -100 重置到 0，實際停在 +25 左右 |
| 會被拒絕 | `CanChangeGoodwillFor` 在玩家攻擊對方據點、或有任務鎖住好感度時回傳 false | 好感度原封不動，例如休戰後仍是 -100，下一次門檻檢查就翻回敵對 |
| 類型改不動 | 對有好感度的派系呼叫 `SetRelationDirect` 只會記一條錯誤 | 關係類型只能跟著好感度門檻走 |

另外，差值是用「上限後的好感度」`GoodwillWith` 算的，卻套在「基礎好感度」上，兩者不一致時結果也會偏掉。

## 2. API

```csharp
using Fortified;

// 設定確切好感度與關係類型（對象預設是玩家）
FactionRelationUtility.SetGoodwillAndKind(fleet, 0, FactionRelationKind.Neutral);

// 只設定好感度；類型依原版門檻從目前類型推得
FactionRelationUtility.SetGoodwill(faction, -100);

// 指定另一個派系、送出關係改變信件、記錄歷史事件（Ideology 戒律會讀到）
FactionRelationUtility.SetGoodwillAndKind(ally, -100, FactionRelationKind.Hostile,
    other: Faction.OfPlayer, canSendLetter: true, reason: MyDefOf.MyHistoryEvent);

// 這對派系能否被強制設定
bool ok = FactionRelationUtility.CanForceRelation(faction, Faction.OfPlayer);

// 與好感度一致的關係類型
FactionRelationKind kind = FactionRelationUtility.KindAgreeingWith(goodwill, desiredKind);
```

`SetGoodwillAndKind` / `SetGoodwill` 回傳是否有套用；遇到硬性限制時回傳 false 且不做任何事。

| 參數 | 預設 | 說明 |
|---|---|---|
| `faction` | 必填 | 要改的派系。 |
| `goodwill` | 必填 | 確切好感度，會夾在 -100 ~ 100。 |
| `kind` | 必填（`SetGoodwillAndKind`） | 想要的關係類型，會依門檻修正，見第 3 節。 |
| `other` | 玩家 | 另一方派系。 |
| `canSendLetter` | false | 關係類型改變時是否送原版的關係變更信件。 |
| `reason` | null | 好感度有變時記錄的 `HistoryEventDef`（只在其中一方是玩家時記錄）。 |

## 3. 行為細節

**雙向同步**：兩邊的 `FactionRelation` 都寫入同一個基礎好感度與類型，和原版一致。

**類型會依門檻修正**。原版 `FactionRelation.CheckKindThresholds` 的規則是：好感度 ≤ -75 為敵對、≥ 75 為盟友；敵對要回到 ≥ 0 才轉中立，盟友要掉到 ≤ 0 才轉中立。要求的類型和好感度不一致時，會改成一致的類型，否則下一次門檻檢查就會翻回去：

| 要求 | 好感度 | 實際結果 |
|---|---|---|
| 中立 | 0 | 中立 |
| 中立 | -80 | 敵對 |
| 盟友 | 30 | 盟友 |
| 盟友 | 80 | 盟友 |
| 敵對 | 10 | 中立 |
| 敵對 | -100 | 敵對 |

**原版通知照常執行**：類型改變時呼叫 `Notify_RelationKindChanged`，所以囚犯身分、地圖上的 Lord、目標快取、不再敵對的任務據點、交易請求、過路商船都會跟著更新。

**繞過的「軟性」限制**：自然好感度的 25% 加成、攻擊據點時的拒絕、任務的好感度鎖定。

**仍遵守的「硬性」限制**（`CanForceRelation`）：
- 任一方沒有好感度（`HasGoodwill` 為 false）
- 任一方已被消滅（`defeated`）
- 永久敵對：`permanentEnemy`、`permanentEnemyToEveryoneExceptPlayer`、`permanentEnemyToEveryoneExcept`

## 4. 注意事項

- **好感度上限仍然存在**：原版的 `GoodwillSituation` 會限制上限，例如玩家正在攻擊對方據點時上限是 -80。這裡寫的是基礎好感度，之後只要有任何好感度變動或情境重算，原版就會依上限把關係翻回敵對。這是原版刻意的行為：攻擊對方等同挑釁。
- **不會發好感度變動訊息**：只有類型改變、而且 `canSendLetter` 為 true 時才有信件。需要告知玩家時請自己發。
- **自然好感度漂移照常**：設定後，原版仍會隨時間把好感度往自然好感度推。

## 5. 框架內的使用

- `ScenPart_ForcedFactionGoodwill`：開局指定好感度改用 `SetGoodwill`，不再溢出。
- DMS：軍事法庭休戰與宣判、封存科技制裁（艦隊敵對、恢復中立、連坐派系）。
