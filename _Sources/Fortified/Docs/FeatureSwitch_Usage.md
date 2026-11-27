# 功能開關與改名對照使用手冊

FFF 是很多模組的共同前置。比較重的系統（有 Harmony 熱路徑補丁、每張地圖都會建立的元件）做成**可開關的功能**：沒有任何模組用到時，補丁不掛、元件不建立。另外 `DefRenameDef` 讓模組改 defName 或類別名稱時，舊存檔還讀得回來。

| 類別 | 用途 |
|---|---|
| `FFF_FeatureDef` | 一項功能：補丁分類、依賴、自動偵測的型別、需要的模組 |
| `FFF_FeatureRequestDef` | 使用者模組宣告要用哪些功能 |
| `FFF_FeatureAttribute` | 標在遊戲會自動建立的型別上：`MapComponent`／`WorldComponent`／`GameComponent`、`Alert`、`SectionLayer`、全圖 `MapDrawLayer`。功能未啟用時不建立 |
| `FFF_FeatureRegistry` | 啟動時決定啟用哪些功能；`IsEnabled(...)` 查詢 |
| `DefRenameDef` | defName 對照（`renames`）與型別對照（`typeRenames`） |

## 功能開關

### 定義一項功能（FFF 內）

```xml
<Fortified.FFF_FeatureDef>
  <defName>FFF_Feature_SeaPlatform</defName>
  <label>sea platform</label>
  <harmonyCategory>FFF.SeaPlatform</harmonyCategory>
  <dependsOn>
    <!-- <li>FFF_Feature_Other</li> -->
  </dependsOn>
  <autoEnableIfDefUses>
    <li>Fortified.SeaPlatformMapExtension</li>
  </autoEnableIfDefUses>
  <requiredModIds>
    <!-- <li>ludeon.rimworld.odyssey</li> -->
  </requiredModIds>
</Fortified.FFF_FeatureDef>
```

| 欄位 | 說明 |
|---|---|
| `harmonyCategory` | 這項功能的補丁類別都標 `[HarmonyPatchCategory("FFF.SeaPlatform")]`。沒有補丁就留空 |
| `dependsOn` | 啟用時一併啟用的功能 |
| `autoEnableIfDefUses` | 非 FFF 本身的 Def 用到這些型別（或子類別）就自動啟用。掃描範圍：Def 本身的類別、`modExtensions`、ThingDef 的 `thingClass` 與 `comps`（含 `compClass`）、WorldObjectDef 的 `worldObjectClass` 與 `comps`、GenStepDef 的 `genStep`、SitePartDef／IncidentDef 的 `workerClass`、MapGeneratorDef 的 `customMapComponents` |
| `requiredModIds` | 需要的模組或 DLC packageId；缺一個就不啟用，依賴它的功能也一起關閉（Log 會警告） |
| `enabledByDefault` | 不需要宣告也啟用。給原本無條件生效、改成功能後不想改變行為的系統用 |

FFF 自己附帶的 Def 不會觸發自動啟用，否則功能永遠會被打開。

### 補丁

```csharp
[HarmonyPatch(typeof(TerrainGrid), nameof(TerrainGrid.SetTerrain))]
[HarmonyPatchCategory("FFF.SeaPlatform")]
public static class Patch_SeaPlatformStructureChanged { ... }
```

`HarmonyEntry` 先 `PatchAllUncategorized()`（沒有分類的補丁照舊全掛），再由 `FFF_FeatureRegistry.Resolve` 對每個啟用的功能 `PatchCategory`。

### 常駐元件

```csharp
[FFF_Feature("FFF_Feature_SeaPlatform")]
public class MapComponent_SeaPlatformSupport : MapComponent { ... }
```

功能未啟用時，`Map`／`World`／`Game` 的 `FillComponents` 之後會把它移除。讀到含有這個元件資料的存檔時，資料會被丟掉；功能未啟用通常代表相依模組已移除，這是預期行為。

**警示與繪圖層也要標**：原版會為 `Alert`、`SectionLayer`、全圖 `MapDrawLayer` 的每個子類別自動建立實例，不經過任何 Def。它們通常會讀功能的元件；功能關閉時元件已被移除，沒標的話每次更新都會丟例外（2026-11-27 實際遇到：`Alert_StrikeMapExpiry` 讀不到被移除的 `GameComponent_StrikeMapTimers`）。標了 `[FFF_Feature]` 後，FFF 在 `AlertsReadout`、`Section` 建構與 `MapDrawer.EnsureGlobalLayersInitialized` 之後把它們移除。經由 Def 建立的東西（Comp、WorkGiver、ITab）不需要標：有 Def 用到功能型別時功能就會自動啟用。

只在特定地圖需要的元件，也可以用 1.7 原版的 `CustomMapComponent`：元件繼承它，再列在 `MapGeneratorDef.customMapComponents`，只有那種地圖才會建立。

### 使用者模組宣告

```xml
<Fortified.FFF_FeatureRequestDef>
  <defName>MyMod_FeatureRequest</defName>
  <features>
    <li>FFF_Feature_SeaPlatform</li>
  </features>
</Fortified.FFF_FeatureRequestDef>
```

只在 C# 裡用到功能型別（沒有 Def 引用）的模組一定要寫，自動偵測抓不到。

### 查詢

```csharp
if (FFF_FeatureRegistry.IsEnabled("FFF_Feature_SeaPlatform")) { ... }
```

結果在 `[StaticConstructorOnStartup]` 階段由 `HarmonyEntry` 決定，之後不會變。比它更早執行的靜態建構子查到的都是 false。

`FFF_FeatureRegistry.EnablersOf(defName)` 傳回啟用這項功能的模組（`FFF_FeatureEnabler`：模組名稱、packageId、怎麼啟用的、經由哪項功能的依賴）；`BlockedReason(defName)` 是有模組要用、卻因缺少必要模組或依賴而關閉的原因。

### 玩家看到的

功能不能手動開關。FFF 的模組設定頁有一份唯讀清單：每項功能一列，顯示由哪些模組啟用；沒有模組用到時顯示「沒有模組使用」，被擋下時顯示原因。滑鼠移到列上會列出每個模組怎麼啟用它（宣告的 `FFF_FeatureRequestDef`、用到功能型別的 Def，或另一項功能的依賴）。啟動時 Log 也會印出同樣的內容。

## 改名對照：`DefRenameDef`

```xml
<Fortified.DefRenameDef>
  <defName>MyMod_Renames</defName>
  <renames>
    <li>
      <defType>WorldObjectDef</defType>
      <oldDefName>MyMod_OldSite</oldDefName>
      <newDefName>MyMod_NewSite</newDefName>
    </li>
  </renames>
  <typeRenames>
    <li>
      <oldTypeName>MyMod.MapComponent_Old</oldTypeName>
      <newType>Fortified.MapComponent_New</newType>
    </li>
  </typeRenames>
</Fortified.DefRenameDef>
```

| 對照 | 掛在哪裡 | 生效範圍 |
|---|---|---|
| `renames` | `BackCompatibility.BackCompatibleDefName` postfix | 存檔與翻譯裡的 defName。舊名稱仍是有效 Def 時不轉換 |
| `typeRenames` | `BackCompatibility.GetBackCompatibleType`、`GetBackCompatibleTypeDirect` postfix | 存檔裡的 `Class` 屬性（Thing、Comp、元件、世界物件、`IExposable`）與 `Type` 欄位。只在原版找不到型別時查表 |

都用 postfix 而不是 `BackCompatibilityConverter`：遊戲版本與模組清單都和存檔相同時，原版會跳過整條轉換鏈，而只更新模組的玩家正好是這種情況。

限制：

- **Def XML 的 `Class` 屬性不經過這裡**（`DirectXmlToObject.ClassTypeOf` 直接找型別，而且那時 `DefRenameDef` 還沒載入），XML 要直接改成新名稱。
- 每一筆都要直接指到最終名稱，不會串接：A → B → C 要寫成 A → C 與 B → C。
- `newType` 必須是存檔欄位型別的子類別，否則忽略並記錄錯誤。
