# TCF Regression Test

## 這是什麼

一個 Python 腳本（`tcf_regression.py`），用 6 個模擬情境驗證 TCF 演算法**沒有退化**。

凍結一次基準後，每次改動 TCF 相關檔案都執行一次，不合格會明確指出哪個情境、哪個指標退化了多少。

---

## 何時必須執行（必要）

改動以下**任一項**都要跑：

| 改動項 | 為什麼 |
|---|---|
| `Services/TemporalCoherenceFilter.cs` | TCF 演算法本體 |
| `Services/ISignalFilter.cs` 的 `FilterParameters` 欄位 | TCF 參數介面 |
| `MainViewModel.cs` 的 `ProcessFrame` 中呼叫 `_signalFilter.Process(...)` 前後的邏輯 | 可能改變送進 filter 的資料 |
| `MainViewModel.cs` 的 `BuildFilterParameters()` | 參數映射改變 |
| `SettingsWindow.xaml` 裡 TCF 滑桿的 `Minimum`/`Maximum`/`Value` | UI 預設值映射改變 |

## 何時不需要執行（節省時間）

這些改動**不會碰到 TCF 行為**，跳過即可：

- 純 UI：色塊、按鈕位置、文字、icon、layout、字型
- 校正視窗（`CalibrationWindow.xaml`/`.xaml.cs`、`CalibrationFitter.cs`）
- 錄製 / 回放（`RecordingService.cs`、錄製 UI）
- 匯出 / PDF / CSV（`ExportService.cs`、`PdfReportService.cs`）
- Legacy filter 內部邏輯（`LegacySignalFilter.cs`）
- 國際化（`LocalizationService.cs`）

---

## 如何使用

### 首次設定（凍結 baseline）

```bash
cd /path/to/tcf_regression_files
python3 tcf_regression.py --freeze
```

這會產生 `regression_baseline.json`，記錄當前 TCF 在 6 個情境的平均指標。
**baseline 應該與 source code 一起 commit**（這樣別人 clone 下來也能跑 regression）。

### 日常驗證

改動 TCF 後：

```bash
python3 tcf_regression.py
```

- Exit code 0 = PASS（所有指標在容忍範圍內）
- Exit code 1 = FAIL（有退化，詳細訊息會列出）
- Exit code 2 = baseline 不存在

詳細輸出模式：

```bash
python3 tcf_regression.py --verbose
```

### 退化處理流程

執行後看到 `✗ REGRESSION DETECTED` 時，有三種可能：

**情況 A：你沒意識到改動造成退化**
→ 這是 regression 的主要用途。回頭檢查改動、修正、再跑。

**情況 B：改動是有意義的設計決策（例如接受某情境略差換取其他情境大幅改善）**
→ 先確認是否全域合理（看完整 diff 表），然後重新凍結 baseline：
```bash
python3 tcf_regression.py --freeze
```
**把新 baseline 連同改動一起 commit**，讓未來的檢查基於新基準。

**情況 C：模擬情境本身不貼近實機**
→ 如果實機測試顯示 TCF 表現很好但 regression 說退化，表示模擬情境需要更新。
改動 `tcf_scenarios.py` 後重新凍結 baseline。但**要格外小心**：模擬與實機差距變大時，regression 的價值會降低。

---

## 容忍度設定

目前 `tcf_regression.py` 內建的容忍度：

| 指標 | 絕對容忍 | 相對容忍 | 意義 |
|---|---|---|---|
| `mae` | 0.3 g | ±15% | 整體誤差小幅波動可接受 |
| `ghost_ratio` | 2% | 不看相對 | Ghost 嚴格：**絕對不得超過 2%** |
| `hole_ratio` | 1% | 不看相對 | Hole 嚴格：**絕對不得超過 1%**（對應你圖 1 的大空洞） |
| `area_err` | 0.1 | ±20% | 面積誤差容忍 |

若要調整，直接改 `tcf_regression.py` 開頭的 `TOLERANCES` 字典。

**實際判定公式**：
```
允許上限 = max(abs_tol, baseline_value × rel_tol)
若 current - baseline > 允許上限 → FAIL
```

**注意**：只檢查「變差」方向（current > baseline）。變好不算退化。

---

## 情境與期望值（目前 baseline）

| 情境 | MAE | Ghost% | Hole% | 說明 |
|---|---|---|---|---|
| Static | 1.12 | 0% | 0% | 靜止壓點 + 中心死點 |
| Dynamic | 1.08 | 0% | 0% | 壓力滑動 |
| DieBonder | 0.75 | 0% | 0% | 壓下→靜止→釋放 |
| Mixed | 1.11 | 0% | 0% | 滑動→停頓→滑走 |
| Fullplate | 1.78 | 0% | 0% | **0.5kg 配重塊**（你圖 1 對應情境） |
| PinArray | 3.11 | 5.94% | 0.88% | **4×4 頂針陣列**（TCFv2 在此略有妥協） |

---

## 局限性（老實說）

1. **模擬永遠不等於實機**。regression PASS 不保證實機不出事；regression FAIL 也可能是模擬情境跟不上。這個工具的價值是「偵測演算法改動造成的相對退化」，不是「驗證演算法本身的絕對正確性」。

2. **只覆蓋 6 個情境**。你實機可能遇到的 corner case（特殊手勢、異形壓力源、極端溫度漂移等）不在測試中。如果發現某類情境常出錯，應該把它加進 `tcf_scenarios.py` 並重凍 baseline。

3. **模擬用固定 random seed**，所以結果完全可重現。但這也意味著若改動剛好在那個 seed 下表現好、其他 seed 差，regression 不會發現。未來若想更嚴謹，可加入多 seed 測試。

4. **容忍度是我猜的**。目前 MAE ±15% 是基於工程常識，不是統計分析。若你實務上發現某個容忍度太嚴或太鬆，自行調整 `TOLERANCES`。

---

## 檔案清單

```
tcf_regression.py          # 主腳本
tcf_scenarios.py           # 6 個壓力情境生成器
tcf_algorithms.py          # Legacy + TCFv1 實作（regression 只用到 metrics）
tcf_v2.py                  # TCFv2 Python 實作（與 C# 對應）
regression_baseline.json   # 凍結的基準（由 --freeze 產生，commit 進 repo）
TCF_REGRESSION.md          # 本文件
```

5 個 `.py` + 1 個 `.json` + 1 個 `.md` = 共 7 個檔案，獨立於主 C# 專案，放在 repo 的 `tools/regression/` 子目錄即可。

---

## 快速 cheat sheet

```bash
# 首次設定
python3 tcf_regression.py --freeze

# 日常檢查
python3 tcf_regression.py

# 看詳細
python3 tcf_regression.py -v

# 改動後重新凍結（確認改動正當才做）
python3 tcf_regression.py --freeze
```
