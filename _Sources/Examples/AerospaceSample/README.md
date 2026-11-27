# FFF Aerospace Sample

FFF 航太框架的範例 mod：不用 Harmony，只參考 `Fortified.dll` 與遊戲本體。說明見 FFF 的 `_Sources/Fortified/Docs/Aerospace_Extension_Usage.md`。

## 內容

| 檔案 | 示範 |
|---|---|
| `Source/CompInfraredSensor.cs`＋`Sample_Buildings.xml` | `IAirSensor`：紅外線搜索感測器 |
| `Source/CompLaserPointDefense.cs`＋`Sample_Buildings.xml` | `IAirEffector`＋`IMapAirEffector`：不走火控、瞬間命中的雷射點防禦，世界層與地圖層都會開火 |
| `Source/WorldObject_ReconAircraft.cs`＋`Sample_AirDefense.xml` | `IAirTrack`：會被防空追蹤與擊落的偵察機；抵達時 `ReportArrival` |
| `Source/GuidanceType_PowerPlant.cs`＋`1.7/Mods/DMSE/` | `GuidanceType`：鎖定發電設施的導引頭（需要 DMSE 的彈體） |
| `Source/AirDefenseExtensions.cs`＋`Sample_AirDefense.xml` | `AirDefenseModifier`、`AirCombatListener`、`AirCombatVeto`（命中率低於 15% 時玩家防空不開火）、`AirDefenseExtensionDef`；開發工具動作 |
| `Sample_AirDefense.xml`＋`Patches/Sample_FactionDoctrine.xml` | `MissileDoctrineDef`：掛到原版海盜的準則 |
| `Sample_Feature.xml` | `FFF_FeatureRequestDef`：開啟 FFF 飛彈功能 |

## 試玩

1. 把整個 `AerospaceSample` 資料夾複製到 `RimWorld/Mods/`，排在 FFF 之後啟用。
2. 在地圖上蓋「IR search sensor」與「laser point-defence emitter」，接上電。
3. 開發者模式 → Debug actions → FFF Aerospace Sample → **Send hostile recon aircraft to current map**。
4. 世界地圖上會出現偵察機；進入感測器範圍後被偵測，飛到據點上空時雷射開火。開發者模式下 Log 會記錄攔截與抵達。
5. 地圖層：觸發敵對空投（例如 Debug actions 的突襲，選空投進場）。感測器會把空投艙收進波次，雷射在落地前開火。

## 建置

```
dotnet build Source/AerospaceSample.csproj -c Release
```

輸出到 `1.7/Assemblies/AerospaceSample.dll`。資料夾不在 `Mods/_Fortified-Framework/_Sources/Examples/` 時，用 `-p:RimWorldDir=<遊戲目錄>\ -p:FortifiedDll=<Fortified.dll 路徑>` 指定參考位置。
