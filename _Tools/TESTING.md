# FFF 自動化測試說明與測試設計原則

本文件說明 Fortified Feature Framework（FFF）目前有哪些自動化檢查、怎麼跑、怎麼看結果，以及之後新增檢查時要遵守的設計原則。

> `_Tools/` 已在 `.rimignore` 中排除，本文件與所有工具都不會上傳到創意工坊。

---

## 1. 現況總覽

FFF 沒有傳統的單元測試專案（沒有 NUnit/xUnit，也沒有 CI）。原因很實際：多數邏輯依賴 RimWorld 執行期（`Verse`、`Find.*`、地圖、Def 資料庫），脫離遊戲很難執行。

所以目前的自動化測試分成兩層：

| 層級 | 工具 | 執行環境 | 抓什麼 |
|---|---|---|---|
| 靜態檢查 | `BuildDLL/build_dlls.py` | 遊戲外，Python + dotnet | C# 編譯錯誤、專案相依順序、輸出 DLL 是否真的更新 |
| 靜態檢查 | `StructureCheck/check_structures.py` | 遊戲外，Python | `FFF_StructureDef` 佈局中電力傳輸器／建築物重疊 |
| 靜態檢查 | `TranslationCheck/check_translations.py` | 遊戲外，Python | 翻譯 key 缺漏、過時、重複、大小寫衝突 |
| 遊戲內手動 | `DebugActions_*.cs` | 開發者模式 → Debug actions → Fortified | 執行期行為（Readiness、Huntdown、結構生成、.rimignore 預覽） |
| 遊戲內手動 | `1.6/Defs/TestDefs.xml` | 取消註解後載入 | 測試用 Def 範例（例如換彈武器） |

三個 Python 工具都只用標準函式庫，不需要 `pip install`。每個資料夾都有一個 `.bat`，雙擊就能跑，報告寫在同一個資料夾。

---

## 2. 各工具說明

### 2.1 BuildDLL：組件建置與編譯檢查

**位置**：`_Tools/BuildDLL/`
**啟動**：雙擊 `run_build.bat`，或把 `.csproj`／模組資料夾拖到 `.bat` 上
**報告**：`BuildReport.txt`

**流程**

1. **探索**：掃描本模組與旁邊的兄弟模組資料夾，收集所有 `.csproj`（略過 `obj/`、`bin/`、`*Backup*` 等，見 `PRUNE_DIRS`）。只有含 `_Source(s)`／`Source(s)`／`src` 或直接含 `.csproj`/`.sln` 的兄弟資料夾才會被當成開發中模組。
2. **排序**：讀取 `AssemblyName`、`OutputPath`、`<ProjectReference>` 與指向其他專案輸出 DLL 的 `<HintPath>`，用 Kahn 演算法拓撲排序。因此 `Fortified` 一定先於 `FortifiedCE` 建置。多目標專案（net472 → 1.6、net10.0 → 1.7）的每個 OutputPath 都會納入。有循環參考時會回報並退回路徑排序，不會中止。
3. **防護**：RimWorld 執行中或 DLL 被鎖定時會先詢問。遊戲會 memory-map `Assemblies\*.dll`，硬寫會留下被截斷的 DLL。
4. **建置**：逐一執行 `dotnet build <proj> -c Release`，每個專案有逾時上限（預設 900 秒）。
5. **報告**：解析 `File.cs(12,34): error CS0103: ...` 格式，去重（MSBuild 每個 target 會重複一次），依檔案分組。

**判定規則**

| 狀態 | 條件 |
|---|---|
| `OK` | exit code 0，且輸出 DLL 存在 |
| `OK` + `NOCHANGE` 警告 | exit code 0，但 DLL 的 mtime/size 沒變（已是最新，或寫入被擋） |
| `FAILED` + `NOOUT` | exit code 0，但宣告的輸出 DLL 不存在 |
| `FAILED` + `TIMEOUT` | 超過 `--timeout` 秒被強制終止 |
| `FAILED` + `EXITn` | exit code 非 0 但解析不到任何診斷，附上輸出尾段 |
| `SKIPPED` | 上游相依專案失敗 |

**常用參數**

```powershell
py _Tools\BuildDLL\build_dlls.py --list         # 只列出建置順序
py _Tools\BuildDLL\build_dlls.py --dry-run      # 跑完整流程但不呼叫 dotnet
py _Tools\BuildDLL\build_dlls.py --self         # 只建本模組
py _Tools\BuildDLL\build_dlls.py Fortified      # 名稱子字串篩選（會自動帶入相依）
py _Tools\BuildDLL\build_dlls.py --keep-going   # 失敗時只跳過下游，繼續其他專案
py _Tools\BuildDLL\build_dlls.py --yes          # 不詢問（無人值守）
py _Tools\BuildDLL\build_dlls.py --warnings     # 報告也列出警告
```

**Exit code**：`0` 全部成功、`1` 至少一個失敗、`2` 環境或用法問題（找不到 dotnet、沒有專案、使用者中止）、`130` Ctrl-C。

---

### 2.2 StructureCheck：結構佈局重疊檢查

**位置**：`_Tools/StructureCheck/`
**啟動**：雙擊 `run_check.bat`，或把模組資料夾拖到 `.bat` 上
**報告**：`StructureOverlaps.txt`

**它在防什麼**

兩個電力傳輸器（電線、電池、發電機…）放在同一格時，RimWorld 在生成地圖時會記錄 `there can't be two transmitters on the same cell`。但這則訊息只寫出生成的 Thing，看不出是哪個佈局放的。這個工具直接指出 Def 名稱與格子座標。

**嚴重度**

| 等級 | 條件 | 執行期後果 |
|---|---|---|
| `ERROR` | 兩個 power transmitter 佔用同一格 | 紅字 log，其中一個無聲地接不上電網 |
| `WARN` | 兩個 edifice 佔用同一格 | 無 log。`WipeMode.VanishOrMoveAside` 會刪掉其中一個，留下牆洞或缺少擺設 |
| `UNRESOLVED` | 找不到該 Def 的佔地資訊 | 該元素被跳過，可能藏著真實重疊 |

**運作方式**

1. 從遊戲的 `Data/`（Core + DLC）與所有掃描到的模組收集 `ThingDef` 的 `size`、是否傳電、`isEdifice`，並沿 `ParentName` 解析繼承。模組的 Def 放在最後讀，與 RimWorld 載入順序一致，同名時模組勝出。
2. 只有在找不到遊戲 `Data/` 時才退回腳本內手寫的 `VANILLA` 表。
3. 對每個 `FFF_StructureDef` 的 `ThingRect`／`ThingScatter`／`FFF_Element_Thing` 元素展開佔用格。計算方式對照 `Verse.GenAdj.OccupiedRect`：**多格物件的 `pos` 是中心格，不是左下角**。3x1 建築放在 x=9 會佔 x=8..10，這是最常見的重疊原因。
4. 電線類 `isEdifice=false`，可以合法與牆或砲塔同格，不計入 edifice 重疊。

**參數**

```powershell
py _Tools\StructureCheck\check_structures.py                     # 本模組 + 所有兄弟模組
py _Tools\StructureCheck\check_structures.py "D:\...\SomeMod"    # 只掃指定資料夾
py _Tools\StructureCheck\check_structures.py --data "D:\...\RimWorld\Data"
py _Tools\StructureCheck\check_structures.py --strict            # 有 ERROR 時 exit 1
```

也可用環境變數 `RIMWORLD_DIR` 指定遊戲路徑。

**Exit code**：預設一律 `0`（讓 `.bat` 能打開報告），要當成閘門用時請加 `--strict`。

---

### 2.3 TranslationCheck：翻譯 key 檢查

**位置**：`_Tools/TranslationCheck/`
**啟動**：雙擊 `run_check.bat`
**報告**：`MissingTranslations.txt`

**檢查項目**

以 `English` 為每個模組的基準語言，對每個其他語言回報：

| 類型 | 意義 |
|---|---|
| `MISSING` | English 有、翻譯沒有 |
| `OBSOLETE` | 翻譯有、English 已經沒有 |
| `DUPLICATE` | 同一語言內同一 key 定義多次 |

另外會先稽核 English 本身：

- 完全重複的 key
- 只差大小寫的 key（例如 `LV1_Initial` 與 `LV1_initial`），這是「幽靈缺漏」的常見來源

**規則細節**

- 會找出 mod 根目錄下**所有** `Languages/` 資料夾（含條件載入的子模組，例如 `CE/`、`Mod/*`），各自獨立比較。
- 範圍為 `Keyed` 與 `DefInjected`。
- 前一個 XML 註解含 `UNUSED`（不分大小寫）的 key 視為刻意保留的廢棄 key，不列入基準。該標記會一直生效到下一個註解為止。
- 若某類別 English 完全沒有 key，則不判定 `OBSOLETE`，避免整批誤報。
- 單一檔案解析失敗只會列在 `PARSE ERRORS`，不會中止整次檢查。

**Exit code**：`0` 正常（即使有缺漏）、`1` 報告寫入失敗、`2` 找不到 mod 根目錄或 `Languages/`。

---

### 2.4 遊戲內 Debug actions（手動驗證）

這些不是自動化測試，但它們是執行期行為唯一的驗證入口。開啟開發者模式後，在 Debug actions → `Fortified` 分類下使用。

| 檔案 | 功能 |
|---|---|
| `StandaloneFunctions/Readiness/DebugActions_Readiness.cs` | 提升／降低戒備等級、加壓力、立即執行動作、部署連隊、在此處發動襲擊或巡邏、記錄狀態 |
| `StandaloneFunctions/Huntdown/DebugActions_Huntdown.cs` | 在目前地圖開始追獵、觸發下一波、停止、暫停 1 天／恢復、記錄狀態 |
| `Structures/Export/DebugActions_Export.cs` | 開啟 FFF 結構匯出工具、切換結構旋轉、生成 `IFFF_Structure`、切換研究探明限制 |
| `StandaloneFunctions/WorkshopIgnore/RimIgnoreDebugActions.cs` | 預覽 `.rimignore` 排除結果（dry-run）、建立範本 |

`1.6/Defs/TestDefs.xml` 存放測試用 Def（目前整份是註解，內容為換彈武器 `CompProperties_AmmoSwitch` 範例）。要用時取消註解，測完再註解回去，避免進到正式版本。

---

## 3. 建議的執行時機

| 時機 | 要跑的工具 |
|---|---|
| 改了 C# | BuildDLL（至少 `--self`；有改公開 API 時跑完整版，順便驗證兄弟模組） |
| 改了 `FFF_StructureDef` 或建築 `ThingDef` 的 `size` | StructureCheck |
| 新增或改名 Keyed / DefInjected key | TranslationCheck |
| 發佈到創意工坊前 | 三個全跑，再用 Debug actions 預覽 `.rimignore` |
| 遊戲版本更新（例如 1.6 → 1.7） | BuildDLL 完整版，StructureCheck 搭配新版 `Data/` |

---

## 4. 測試設計原則

以下原則是從現有三個工具歸納出來的，新增檢查工具時請遵守。

### 4.1 指出源頭，而不是症狀

遊戲 log 往往只告訴你「哪個生成出來的東西壞了」。檢查工具的價值在於直接指出 **哪個 Def、哪個檔案、哪一格／哪一行**。

- StructureCheck 回報 `Def 名稱 + 格子座標 + 檔案路徑`，而不是 `Battery43189`。
- BuildDLL 依檔案分組並附行號，而不是丟出整片 MSBuild 輸出。

### 4.2 模擬遊戲的真實規則，不要自己發明

判定邏輯要對照 RimWorld 原始碼，並在程式碼註解寫出對應的遊戲方法。

- 佔用格計算對照 `Verse.GenAdj.OccupiedRect`（中心格規則）。
- Def 繼承沿 `ParentName` 解析；同名 Def 以模組優先，對照載入順序。
- 重複 key 以「最後寫入者勝」計值，與 RimWorld 一致。

規則不確定時，先用 decompiler 查原始碼再寫，不要憑印象。

### 4.3 優先讀真實資料，手寫表只當後備

能從遊戲 `Data/` 或模組 XML 讀到的資訊就不要手寫。手寫表永遠會落後遊戲版本。

- StructureCheck 自動往上找 `RimWorld/Data`，只有找不到時才用 `VANILLA` 表，而且報告會明確寫出用了後備表。

### 4.4 區分嚴重度，誤報與漏報都要說清楚

- **ERROR**：一定是 bug，執行期會出錯或 log 噴紅。
- **WARN**：執行期不報錯，但結果不符預期。
- **UNRESOLVED／NOTE**：工具無法判斷的部分要列出來，不要默默跳過。看不到的東西必須被看見。

當資料不足以判斷時寧可不報（例如 English 沒有某類別 key 就不判 `OBSOLETE`），並在報告中說明原因。

### 4.5 不能被單一壞檔打斷

一個解析失敗的 XML 或 `.csproj` 不應讓整次檢查失敗。

- 壞檔記錄到 `PARSE ERRORS`／`unreadable project`，其餘照常跑完。
- 輸出用 UTF-8 並以 `errors="replace"` 防護，Windows 主控台（cp950）的奇怪字元不能讓工具崩潰。

### 4.6 安全第一：唯讀，或在破壞前先擋

- 檢查工具一律唯讀，只寫自己的報告檔。
- 會寫檔的工具（BuildDLL）在可能造成損壞前先防護：偵測 RimWorld 是否執行中與 DLL 是否被鎖。
- 「成功」要驗證結果，而不是只看 exit code：BuildDLL 會比對建置前後 DLL 的 mtime/size，抓出「回報成功但沒寫出」的情況。

### 4.7 零安裝、雙擊可跑

- 只用 Python 標準函式庫。
- 每個工具附一個 `.bat`：自動找 `py` 或 `python`、設定 `chcp 65001`、跑完自動打開報告。
- 支援拖放資料夾或檔案到 `.bat` 上縮小範圍。

### 4.8 結果寫成報告檔，且可重現

- 報告固定寫在工具旁（`BuildReport.txt`、`StructureOverlaps.txt`、`MissingTranslations.txt`），可以 diff 前後兩次。
- 報告開頭列出掃描範圍、資料來源、產生時間與總計。
- 排序要穩定（依路徑、依座標），同樣的輸入產生同樣的報告。

### 4.9 框架要驗證依賴它的模組

FFF 是框架，改動可能壞掉的是下游內容模組。所以預設掃描範圍是 **本模組 + 兄弟模組**，並提供 `--self` 或指定路徑來縮小範圍。

### 4.10 Exit code 要有意義，也要能當閘門

| Code | 意義 |
|---|---|
| `0` | 正常完成 |
| `1` | 檢查到需要處理的失敗 |
| `2` | 環境或用法錯誤 |
| `130` | 使用者中斷 |

人看報告的工具（StructureCheck、TranslationCheck）預設回 `0`，讓 `.bat` 能打開報告。需要當自動化閘門時提供 `--strict` 之類的參數。

### 4.11 提供不執行副作用的預覽模式

需要呼叫外部程式或寫檔的工具，要有 `--list`／`--dry-run` 可以先看會做什麼。遊戲內也一樣，例如 `.rimignore` 的「預覽排除結果」。

### 4.12 允許刻意的例外，但要明確標記

例外要寫在資料本身，讓人一眼看懂，而不是藏在工具設定裡。例如翻譯檔中用 `<!-- UNUSED -->` 註解標記刻意保留的 key。

---

## 5. 新增檢查工具的清單

1. 建立 `_Tools/<名稱>/`，放 `<名稱>.py`、`run_check.bat`，報告寫在同一資料夾。
2. 檔頭 docstring 寫清楚：防什麼問題、遊戲裡的症狀、用法、exit code。
3. 判定規則對照遊戲原始碼，並在註解寫出對應方法。
4. 遍歷資料夾時剪掉 `obj/`、`bin/`、`.git/`、`Textures/` 等大型非 Def 目錄（參考既有的 `PRUNE_DIRS`）。
5. 壞檔進錯誤區段，不要中止。
6. 報告分 ERROR／WARN／UNRESOLVED，開頭列出範圍與總計。
7. 拿一個已知有問題的模組實測，確認能抓到；再拿一個乾淨的模組實測，確認沒有誤報。
8. 更新本文件的第 1、2、3 節。
