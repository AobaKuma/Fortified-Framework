# RimWorld 1.7 更新說明

Fortified Features Framework 已同時支援 RimWorld 1.6 與 1.7，兩個版本由同一份原始碼建置。
本文整理 1.7 帶來的變化、框架為此做的調整，以及之後維護與發佈時需要注意的事項。

> 針對版本：RimWorld 1.7.4976 rev1043（Assembly-CSharp 1.7.9781.31297）

---

## 1. 1.7 有什麼不同

1.7 最大的變化不在遊戲內容，而在底層引擎：

| 項目 | 1.6 | 1.7 |
|---|---|---|
| 引擎 | Unity（Mono） | Waylith（CoreCLR） |
| 執行環境 | .NET Framework 4.7.2 | .NET 10 |
| 繪圖 API | D3D11 / Metal / OpenGL / Vulkan | Vulkan |
| UnityEngine 型別 | Unity 本體 | 由遊戲根目錄的 `UnityStubs.dll` 提供（相容層） |
| 遊戲 DLL 位置 | `RimWorldWin64_Data/Managed/` | 遊戲根目錄（`Assembly-CSharp.dll` 等） |
| Harmony | 2.4.x | 3.0（Harmony 模組 1.7 資料夾，等同 NuGet `Lib.Harmony 3.0.0-rc.2`） |
| 遊戲記錄檔 | `…/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Player.log` | 同資料夾下的 `Logs/Player.log` |

遊戲本身的 C# API 與 1.6 幾乎相同：Def 類別沒有移除任何欄位，原版 XML 結構也沒有影響本框架的變動。
真正需要處理的是**建置方式、Harmony 3 的嚴格檢查、著色器格式與創意工坊上傳流程**。

其他值得知道的 1.7 新功能：

- 新增 `ShaderDef`（GLSL 著色器 + XML 定義，放在載入資料夾的 `Shaders/`）與 `MaterialDef`。
- 上傳創意工坊時可烘焙「資源包」`Assets.zip`（預設開啟）。
- 命令列新增 `-mod=packageId1;packageId2`，可直接指定啟用的模組（另有原本的 `-quicktest`、`-savedatafolder=`）。
- 第一次以 1.7 啟動時，若模組設定來自 1.6，遊戲會**重設模組清單**為只有原版與 DLC，需要重新啟用模組。

---

## 2. 框架做了哪些調整

### 2.1 建置：一個專案、兩個版本

`_Sources/Fortified/Fortified.csproj` 改為多目標建置：

| 目標框架 | 對應版本 | 輸出 | 參考來源 |
|---|---|---|---|
| `net472` | 1.6 | `1.6/Assemblies/Fortified.dll` | NuGet：`Krafs.Rimworld.Ref`、`Lib.Harmony 2.4.2`、`RimWorld.MultiplayerAPI` |
| `net10.0` | 1.7 | `1.7/Assemblies/Fortified.dll` | 遊戲本體 DLL（`Assembly-CSharp`、`UnityStubs`、`Waylith`）、`Lib.Harmony 3.0.0-rc.2`、`1.7/Assemblies/0MultiplayerAPI.dll` |

`_Sources/FortifiedCE/FortifiedCE.csproj` 同樣改為多目標建置：`net472` → `1.6/CE/Assemblies`、`net10.0` → `1.7/CE/Assemblies`。
CE 目前只有 1.6 版（.NET Framework），兩個目標都參考 `_Sources/CombatExtended.dll`；1.7 下 CE 由遊戲的相容層載入。

- CE 的 API 用到的 Unity 型別（`Vector3`、`Color`…）記在 `UnityEngine.CoreModule` 名下，而 1.7 的這些型別在 `UnityStubs`。
  遊戲執行時會把 `UnityEngine.*` 重導到 `UnityStubs`，編譯器卻不會，直接以 `net10.0` 參考 CE 會出現 27 個 CS0012。
- 解法是只供編譯使用的轉送組件 `_Sources/UnityCoreModuleFacade`：組件名稱為 `UnityEngine.CoreModule`，
  把 CE 用到的 32 個型別轉送（`TypeForwardedTo`）到 `UnityStubs`。FortifiedCE 的 `net10.0` 目標以 `ProjectReference`（`Private="false"`）參考它，
  不會複製到輸出資料夾；編出的 `FortifiedCE.dll` 直接參考 `UnityStubs`，不參考 `UnityEngine.CoreModule`。

- 1.7 建置會額外定義 `RW_1_7`，需要分版本的程式碼請用 `#if RW_1_7`。
- `dotnet build` 或 `build.bat` 一次產出兩個 DLL；`_Tools/BuildDLL/build_dlls.py` 已能辨識多目標專案的兩個輸出（相依排序、檔案鎖定檢查、建置報告都涵蓋兩者）。

### 2.2 原始碼修正

- **移除 1.7 不存在的 `using`**（11 個檔案）：`NVorbis`、`RuntimeAudioClipLoader`、`Mono.Math`、`Mono.Security.X509`、`System.Runtime.Remoting.Messaging`、`UnityEngine.Assertions.Must`。這些都是 IDE 自動加入、實際沒用到的命名空間，移除後對 1.6 無影響。
- **修正 `Patch_LordToil_PrepareCaravan_GatherItems`**：`__instance` 的型別誤寫為 `LordToil_PrepareCaravan_GatherDownedPawns`。Harmony 2 會放行，Harmony 3 會直接擲出例外；而 `PatchAll` 遇到例外會中止，**排在它之後的所有補丁都不會套用**。1.6 版也一併受益於此修正。
- **`.rimignore` 上傳過濾適配 1.7**（見 2.5）。

### 2.3 著色器（塗裝系統）

1.7 讀取 AssetBundle 內的著色器時，只能使用 **Vulkan（SPIR-V）變體**：

| 檔案 | 內含平台 | 1.7 可用 |
|---|---|---|
| `fortified_shaders_win` | D3D11 | ✗ |
| `fortified_shaders_mac` | Metal | ✗ |
| `fortified_shaders_linux` | OpenGLCore + Vulkan | ✓ |

1.7 仍依作業系統後綴挑選 bundle（Windows 讀 `_win`），因此 `1.7/AssetBundles/` 內的三個檔案都是 **linux 版的複本**。
`UnityProject/Assets/Editor/BuildBundle.cs` 已改為建置完成後自動把 linux 版複製成 1.7 的三個檔名。

若著色器無法載入，`FFF_AssetLoader.PaintShader` 會是 null，塗裝會退回原版著色器（不會報錯，但迷彩、圖示、疊加層效果會消失）。

### 2.4 資料夾結構

```
LoadFolders.xml
  v1.6 → /, 1.6, (CE 啟用時) 1.6/CE
  v1.7 → /, 1.6, (CE 啟用時) 1.6/CE, 1.7, (CE 啟用時) 1.7/CE
```

- 1.7 **直接沿用 1.6 的 Defs 與 Patches**。
- 列在後面的資料夾優先，**相同相對路徑的檔案會蓋掉前面的版本**：
  - `1.7/Assemblies/Fortified.dll`、`0MultiplayerAPI.dll` 蓋掉 1.6 的 .NET Framework 版本；
  - `1.7/CE/Assemblies/FortifiedCE.dll` 蓋掉 `1.6/CE` 的版本（遊戲以 `Assemblies/` 下的相對路徑去重，只載入最後列出的那一份）；
  - `1.7/AssetBundles/fortified_shaders_*` 蓋掉 1.6 的 bundle。
- `About.xml` 的 `supportedVersions` 已加入 `1.7`。

### 2.5 創意工坊上傳與 `.rimignore`

1.7 的上傳確認視窗預設勾選「建立資源包」，流程變為：

1. 把每個載入資料夾的 `Textures`／`Sounds` 烘焙成該資料夾內的 `Assets.zip`（附屬檔 `Assets.zip.*` 只供本機使用）；
2. 把整個模組複製到系統暫存資料夾；
3. 上傳那份複本，結束後刪除。

框架的 `.rimignore` 功能已配合調整：偵測到這份複本時，直接在複本中刪除排除項目並驗證檔案數，保留原版的烘焙結果；取消或失敗時會請原版清除複本。

另外調整了本模組自己的設定：

- `.rimignore` 原本的 `*.zip` 會把 `Assets.zip` 一起排除，已加上 `!Assets.zip`，並排除 `Assets.zip.*`。
- `.gitignore` 忽略 `Assets.zip`、`Assets.zip.*`（每次上傳都會重新產生，不需進版控）。

詳細設計見 [RimIgnore.md](RimIgnore.md)。

---

## 3. 驗證結果

| 項目 | 結果 |
|---|---|
| 1.6 / 1.7 編譯 | 皆 0 錯誤（1 個既有警告：`FacilityEntrance.cs` CS8073） |
| Harmony 補丁（以 Harmony 3 對 1.7 實際套用） | 139/139 個補丁類別成功；手動補丁（CovertOps、SensorDoor、DisableShuttleLaunch）成功 |
| Transpiler | 5 個全部確實改寫到目標 IL |
| 被補丁方法的 1.6 → 1.7 差異 | 150 個中 148 個完全相同；2 個有變動且已確認無影響或已處理 |
| XML | 所有 `ParentName`、`Class`／`thingClass`／`compClass` 等型別、XPath 補丁目標在 1.7 皆存在 |
| `.rimignore` 過濾 | 以模擬的 1.7 上傳複本測試通過（保留 `Assets.zip`、排除附屬檔與開發資料夾） |
| FortifiedCE 1.6 / 1.7 編譯 | 皆 0 錯誤 0 警告；兩個版本的型別清單與發佈中的 1.6 版完全相同 |
| FortifiedCE Harmony 補丁（靜態檢查） | 9 個補丁類別的目標在 1.7 本體、工作坊版 CE（2026-07-04）、本框架中皆存在，`__instance` 型別與宣告類別一致；無 Transpiler |

**尚未在遊戲內實測**：實際載入 XML、`StaticConstructorOnStartup` 初始化、著色器在 Vulkan 下的轉換結果，仍需開遊戲確認。

建議的冒煙測試（不影響平常的存檔與模組設定）：

```bash
"D:\Steam\steamapps\common\RimWorld\RimWorld.exe" -savedatafolder=<暫存資料夾> -mod=brrainz.harmony;AOBA.Framework -quicktest
```

確認重點：記錄檔沒有 Fortified 相關錯誤、出現 `[Fortified] 涂装着色器加载成功`、塗裝視窗的預覽正常。

---

## 4. 已知限制

- **Combat Extended 尚無 1.7 版**：1.7 的 FortifiedCE 是對 CE 1.6 的 DLL 建置，靠遊戲的相容層執行，尚未在遊戲內實測。
  CE 釋出 1.7 版（.NET 10）後，把 FortifiedCE 的 `net10.0` 目標改參考 CE 1.7 的 DLL 重新建置；屆時 CE 不再參考 `UnityEngine.CoreModule`，`UnityCoreModuleFacade` 可以移除。
- **Multiplayer**：沿用 `0MultiplayerAPI.dll` 0.6.0（.NET Framework 版，1.7 可正常載入）；Multiplayer 模組本身尚未支援 1.7。
- **塗裝著色器**仰賴 Unity bundle 的 Vulkan 轉換，尚未在遊戲內目視確認。

---

## 5. 維護注意事項

### 建置環境

- 需要 **.NET 10 SDK**（1.6 目標仍需 .NET Framework 4.7.2 參考組件，由 NuGet 提供）。
- 1.7 建置直接參考遊戲 DLL，預設模組位於 `<RimWorld>/Mods/<本模組>/`。遊戲裝在別處時：
  ```bash
  dotnet build Fortified.csproj -c Release -p:RimWorldDir=D:\path\to\RimWorld\
  ```
- **遊戲執行中不要建置**：遊戲會鎖住已載入的 DLL，寫入會失敗或產生損壞的檔案。`build_dlls.py` 會檢查並提示。

### 撰寫跨版本程式碼

- 兩個版本的 API 差異極小，絕大多數程式碼不需要分版本；真的需要時用 `#if RW_1_7`。
- **不要保留沒用到的 `using`**，尤其是反編譯程式碼貼上時附帶的整串命名空間，1.7 少了一些組件就會編譯失敗。
- **Harmony 3 比 Harmony 2 嚴格**：
  - `__instance`、`__result` 等注入參數的型別必須正確；
  - 一個補丁類別失敗會讓 `PatchAll` 中止，後面的補丁全部失效；
  - 新增補丁後，務必兩個版本都開遊戲確認記錄檔沒有 `Patching exception`。
- 以字串指定的反射目標（`AccessTools.Field("…")`、編譯器產生的 `<>c__DisplayClass…` 名稱等）每次遊戲改版都可能失效，更新時需逐一確認。

### 資料夾與 XML

- **1.6/Assemblies 若新增 DLL，1.7/Assemblies 必須有同名的 1.7 版本**，否則 1.7 會載入 .NET Framework 版的 DLL。
- 只在 1.7 需要不同的 XML 時，把檔案放到 `1.7/` 下**相同的相對路徑**（例如 `1.7/Defs/StatDef.xml`）即可覆寫 1.6 的版本。
- 1.6 專屬內容放在 `1.6/` 的子資料夾，並只在 `v1.6` 的載入清單列出。
- CE 相容內容放在 `1.6/CE`，`v1.6` 與 `v1.7` 都會載入；`1.7/CE` 只放 .NET 10 版的 `FortifiedCE.dll`。
- 更新 `_Sources/CombatExtended.dll` 後，若 1.7 編譯出現「類型 'X' 定義在未參考的組件中 'UnityEngine.CoreModule'」（CS0012），
  在 `_Sources/UnityCoreModuleFacade/TypeForwards.cs` 補上 `X` 即可。

### 著色器

- 修改 `UnityProject/Assets/Shaders/` 後，用選單 `Assets/Build Fortified AssetBundle` 重新建置，1.6 與 1.7 的 bundle 會一起更新。
- **不要把 Unity 專案 Linux 平台的 Graphics API 改成不含 Vulkan**，否則 1.7 會失去塗裝著色器。
- 長期可考慮改用 1.7 原生的 `ShaderDef` + GLSL（參考遊戲 `Data/Core/Shaders/` 與 `Data/Core/Defs/ShaderDefs/`），不再依賴 Unity。

### 上傳創意工坊

- 「建立資源包」建議保持勾選，1.7 玩家的貼圖載入會比較快。
- **不要勾選「略過原始貼圖」**：被打包的 PNG 不會上傳，而 1.6 讀不懂 `Assets.zip`，1.6 玩家會缺貼圖。
- 上傳後模組資料夾內會出現 `Assets.zip`，屬於產生物，已被 `.gitignore` 忽略。

### 查閱遊戲程式碼

- RimSage 目前仍是 1.6 的資料，查 1.7 請用反編譯工具開啟遊戲根目錄的 `Assembly-CSharp.dll`、`UnityStubs.dll`。
- 需要比對 1.6 時，完整的 1.6 最終版 `Assembly-CSharp.dll`（1.6.4871）仍保留在
  `Mods/_Dead-Man-Switch-Taktikmagier/.source/Militarmagier/bin/Debug/Assembly-CSharp.dll`。

---

## 6. 給依賴本框架的模組作者

- 1.7 請參考 `1.7/Assemblies/Fortified.dll`，1.6 請參考 `1.6/Assemblies/Fortified.dll`；兩者的公開 API 相同。
- 建置設定可直接參考本框架的 `Fortified.csproj`。常見的坑：
  - `RimWorld.MultiplayerAPI` 的 NuGet 套件會連帶引入 1.6 的 Unity 參考組件，在 `net10.0` 下造成型別重複（CS0433），1.7 請直接參考 `0MultiplayerAPI.dll`；
  - 不要在 `net10.0` 目標引用 `Krafs.Rimworld.Ref`；
  - 以 `net10.0` 參考只有 1.6 版的模組 DLL（CE、VEF…）時，若它的 API 用到 Unity 型別會出現 CS0012，
    可比照 `_Sources/UnityCoreModuleFacade` 做一個只供編譯的轉送組件；只碰到一兩處時，也可以在 `#if RW_1_7` 下改用反射讀取。
- 自己的 AssetBundle 著色器同樣需要 Vulkan 變體（使用 linux 版 bundle）。

---

## 7. 本次更新的檔案

| 檔案 | 內容 |
|---|---|
| `About/About.xml` | 加入 1.7 |
| `LoadFolders.xml` | 新增 `v1.7` |
| `1.7/Assemblies/` | 1.7 版 `Fortified.dll`、`0MultiplayerAPI.dll` |
| `1.7/AssetBundles/` | Vulkan 著色器 bundle（linux 版複本 ×3） |
| `1.6/Assemblies/Fortified.dll` | 以修正後的原始碼重新建置 |
| `_Sources/Fortified/Fortified.csproj` | 多目標建置 |
| `_Sources/Fortified/build.bat` | 輸出訊息 |
| `_Sources/Fortified/**/*.cs` | 移除無效 `using`（11 個檔案）、修正 GatherItems 補丁 |
| `StandaloneFunctions/WorkshopIgnore/*.cs` | 適配 1.7 上傳流程 |
| `_Sources/Fortified/Docs/RimIgnore.md` | 補充 1.7 上傳流程 |
| `UnityProject/Assets/Editor/BuildBundle.cs` | 自動產生 1.7 bundle |
| `_Tools/BuildDLL/build_dlls.py` | 支援多目標專案 |
| `.rimignore`、`.gitignore` | `Assets.zip` 相關規則 |
| `LoadFolders.xml` | `v1.7` 在 CE 啟用時載入 `1.6/CE`、`1.7/CE` |
| `1.7/CE/Assemblies/` | 1.7 版 `FortifiedCE.dll` |
| `_Sources/FortifiedCE/FortifiedCE.csproj` | 多目標建置 |
| `_Sources/UnityCoreModuleFacade/` | 只供編譯的 `UnityEngine.CoreModule` 轉送組件 |
