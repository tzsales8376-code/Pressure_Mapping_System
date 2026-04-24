# TCF Regression Test — v1

## 1. 檔案對照表

| 交付檔案 | 目標路徑 | 類型 |
|---|---|---|
| `tcf_regression.py` | `tools/regression/`（新子目錄） | **新檔** |
| `tcf_scenarios.py` | `tools/regression/` | **新檔** |
| `tcf_algorithms.py` | `tools/regression/` | **新檔** |
| `tcf_v2.py` | `tools/regression/` | **新檔** |
| `regression_baseline.json` | `tools/regression/` | **新檔**（凍結的基準，需 commit） |
| `TCF_REGRESSION.md` | `tools/regression/` | **新檔**（詳細使用文件） |

建議路徑 `tools/regression/`，但你也可以放 `tests/` 或任何你習慣的位置。只需保持這 6 個檔在同一目錄即可。

---

## 2. 被覆蓋檔案的具體變更清單

**無**。本次交付全部是新檔案，不會動到 `src/PressureMappingSystem/` 下任何程式碼。

---

## 3. 向後相容性說明

| 問題 | 答案 |
|---|---|
| 影響主專案建置？ | **完全不影響**。全是 Python 檔，與 C# 專案平行獨立 |
| 影響主專案執行期？ | **不影響**。只有你手動執行 `python3 tcf_regression.py` 才會跑 |
| 有新依賴？ | Python 端需要 `numpy` 與 `scipy`。若你機器上已經能跑 Python 算法驗證就已經都有 |
| 影響既有 CI/CD？ | 我沒有幫你接 CI。若你要接，本腳本 `sys.exit(0/1/2)` 可直接嵌入 GitHub Actions / Jenkins |

---

## 4. 建置後的驗證建議

### 4.1 初次使用流程

把這 6 個檔放到 repo 的 `tools/regression/`（或你指定的位置），然後：

```bash
cd tools/regression
python3 tcf_regression.py
```

**預期**：看到 6 個情境的結果表，最下方顯示 `✓ ALL SCENARIOS WITHIN TOLERANCE`。

如果因為你本機 Python 版本 / numpy 版本差異導致數值微幅不同而 FAIL，這是合理的：
執行 `python3 tcf_regression.py --freeze` 在你機器上重新凍結一次，以後的 regression 以你機器為基準。

### 4.2 模擬破壞測試（驗證 regression 機制真的會抓到退化）

在沒有改動任何 C# 程式碼的前提下，故意改壞 Python TCFv2 實作，確認 regression 抓得到：

```bash
# 暫時註解 tcf_v2.py 裡 process() 的 InpaintPass1 與 InpaintPass2 呼叫
python3 tcf_regression.py
# 應該看到 Fullplate 的 hole_ratio 與 MAE 都 FAIL
```

測完記得還原 `tcf_v2.py`。

### 4.3 日常使用時機

對照 `TCF_REGRESSION.md` 的「何時必須執行」清單判斷。改完 TCF 相關 C# 檔案、build 通過後，把該改動同步到 `tcf_v2.py`，然後跑 regression。

**這是最重要但也最容易被忽略的一步**：C# 改動不會自動反映到 Python 模擬。你必須手動同步 `tcf_v2.py` 才能真正驗證。若不同步，regression 會 PASS 但實機 TCF 可能已經退化。

---

## 5. 沙盒無法驗證的風險

| 項目 | 說明 |
|---|---|
| **Python 版本差異** | 我在 Python 3.12 / numpy / scipy 測試。若你環境差異大，凍結的 baseline 數值可能略有不同 |
| **浮點運算不完全確定性** | 不同 CPU / numpy 版本下浮點運算極少數位元可能不同；容忍度設計已考慮這點 |
| **Python TCFv2 vs C# TCFv2 可能不完全等價** | 兩邊是我分別實作的，邏輯相同但語言差異可能造成極微細差距。目前沒發現但無法完全保證 |

---

## 6. 測試驗證結果

我已在沙盒內執行 4 次確認機制正確：

1. `--freeze` 寫入 baseline：成功
2. 無改動時執行 check：PASS（exit 0）
3. 故意移除 `_remove_small_islands`：PinArray MAE 反而小幅下降（-0.095），PASS —— 證明容忍度對小幅波動不過敏
4. 故意移除 `_inpaint_pass1 + _inpaint_pass2`：Static MAE 升 +0.350 FAIL、Fullplate MAE 升 +1.572 FAIL、Fullplate hole_ratio 從 0% 變 1% FAIL，exit code 1 —— 證明**對關鍵指標（hole_ratio）的退化會正確抓到**，特別是你圖 1 對應的大空洞問題
