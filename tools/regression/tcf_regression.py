#!/usr/bin/env python3
"""
TCF Regression Test

當你改動以下任一項，就該執行這個腳本：
  - Services/TemporalCoherenceFilter.cs
  - Services/ISignalFilter.cs 裡的 FilterParameters 欄位
  - MainViewModel.ProcessFrame 裡 filter 呼叫前後的邏輯
  - SettingsWindow 裡 TCF 滑桿參數映射

不需要執行的情況（改動不會影響 TCF 行為）：
  - 純 UI 顏色、按鈕位置、文字
  - 校正視窗（CalibrationWindow）
  - 錄製、匯出、PDF 報告
  - Legacy filter 內部邏輯

用法：
  # 凍結當前結果為 baseline（僅首次使用或確定要更新基準時）
  python3 tcf_regression.py --freeze

  # 以 baseline 驗證當前結果
  python3 tcf_regression.py
  # 或顯示詳細指標
  python3 tcf_regression.py --verbose

退出碼：
  0 = PASS（所有情境在容忍度內）
  1 = FAIL（有情境退化）
  2 = 腳本錯誤 / baseline 不存在
"""
import argparse
import json
import sys
from pathlib import Path

import numpy as np

import tcf_scenarios as sc
from tcf_algorithms import metrics
from tcf_v2 import TcfV2Filter


# ============================================================
#  配置
# ============================================================

# 六個情境：名稱, 產生器, 幀數
SCENARIOS = [
    ("Static",    sc.scenario_static,             60),
    ("Dynamic",   sc.scenario_dynamic,            60),
    ("DieBonder", sc.scenario_diebonder,          90),
    ("Mixed",     sc.scenario_mixed,              90),
    ("Fullplate", sc.scenario_fullplate_static,   60),
    ("PinArray",  sc.scenario_diebonder_array,    60),
]

# 容忍度設定
# 對每個指標定義：
#   abs_tol: 絕對值容忍度（數值變化超過這個就算退化）
#   rel_tol: 相對值容忍度（比 baseline 多於這個比例就算退化）
# 實際判定：變化超過 max(abs_tol, baseline × rel_tol) → 退化
TOLERANCES = {
    "mae":         {"abs_tol": 0.3, "rel_tol": 0.15},   # MAE 容忍 ±15%
    "ghost_ratio": {"abs_tol": 0.02, "rel_tol": 0.0},   # Ghost 嚴格：絕對值不得超過 2%
    "hole_ratio":  {"abs_tol": 0.01, "rel_tol": 0.0},   # Hole 嚴格：絕對值不得超過 1%
    "area_err":    {"abs_tol": 0.1, "rel_tol": 0.20},   # Area 容忍 ±20%
}

BASELINE_PATH = Path(__file__).parent / "regression_baseline.json"


# ============================================================
#  核心：跑一輪所有情境、回傳指標
# ============================================================

def run_scenarios(seed_override=None):
    """
    跑 6 個情境，回傳 { scenario_name: {mae, ghost_ratio, hole_ratio, area_err} }
    用每個情境整段幀的平均作為該情境的指標。
    """
    results = {}
    for name, scenario_fn, nframes in SCENARIOS:
        # 情境產生器內部用固定 seed，可重現
        gt, obs = scenario_fn(n_frames=nframes)

        tcf = TcfV2Filter(fps=60, response_ms=80, noise_suppression=50,
                          noise_floor=5.0)
        outs = []
        for t in range(nframes):
            outs.append(tcf.process(obs[t]))
        outs = np.array(outs)

        # 跳過 cold-start 前幾幀（ring buffer 填充期）
        # response_ms=80, fps=60 → ring buffer 至少 5 幀才開始 TCF
        # 保守從第 10 幀開始算平均指標
        skip = 10

        # 計算所有幀的指標，取平均
        ms = [metrics(gt[t], outs[t]) for t in range(skip, nframes)]
        avg = {
            k: float(np.mean([m[k] for m in ms]))
            for k in ["mae", "ghost_ratio", "hole_ratio", "area_err"]
        }
        results[name] = avg
    return results


# ============================================================
#  Freeze：寫入 baseline
# ============================================================

def freeze():
    print("Running scenarios to freeze baseline...")
    results = run_scenarios()

    # 附上 metadata
    baseline = {
        "_meta": {
            "version": "tcf_v2",
            "description": "TCF v2 regression baseline — 凍結時的六情境平均指標",
            "tolerances": TOLERANCES,
        },
        "scenarios": results,
    }

    BASELINE_PATH.write_text(json.dumps(baseline, indent=2, ensure_ascii=False))
    print(f"\n✓ Baseline frozen at: {BASELINE_PATH}\n")
    print_table(results, title="Frozen values")


# ============================================================
#  Check：比較當前與 baseline
# ============================================================

def check(verbose=False):
    if not BASELINE_PATH.exists():
        print(f"✗ Baseline not found: {BASELINE_PATH}")
        print("  First run: python3 tcf_regression.py --freeze")
        return 2

    baseline = json.loads(BASELINE_PATH.read_text())
    base_scenarios = baseline["scenarios"]

    print("Running current TCF against baseline...\n")
    current = run_scenarios()

    # 逐情境、逐指標比對
    failures = []
    for name in base_scenarios:
        if name not in current:
            failures.append(f"  Scenario '{name}' missing in current run")
            continue

        for metric, tol in TOLERANCES.items():
            base_val = base_scenarios[name][metric]
            cur_val = current[name][metric]
            abs_change = cur_val - base_val  # 正 = 變差（MAE/ghost/hole/area_err 都是越小越好）

            abs_tol = tol["abs_tol"]
            rel_tol = tol["rel_tol"]
            threshold = max(abs_tol, base_val * rel_tol)

            if abs_change > threshold:
                failures.append(
                    f"  [{name}] {metric}: {base_val:.3f} → {cur_val:.3f} "
                    f"(+{abs_change:.3f}, tolerance {threshold:.3f})"
                )

    # 輸出結果表
    print_comparison_table(base_scenarios, current, verbose=verbose)

    if failures:
        print("\n" + "=" * 68)
        print("✗ REGRESSION DETECTED")
        print("=" * 68)
        for f in failures:
            print(f)
        print("\nIf the degradation is intentional (e.g. a deliberate design change),")
        print("re-freeze the baseline: python3 tcf_regression.py --freeze")
        return 1

    print("\n" + "=" * 68)
    print("✓ ALL SCENARIOS WITHIN TOLERANCE")
    print("=" * 68)
    return 0


# ============================================================
#  輸出格式化
# ============================================================

def print_table(results, title=""):
    if title:
        print(f"\n{title}")
    header = f"{'Scenario':<14} {'MAE':>8}  {'Ghost%':>8}  {'Hole%':>8}  {'Area err':>10}"
    print("-" * len(header))
    print(header)
    print("-" * len(header))
    for name, m in results.items():
        print(f"{name:<14} {m['mae']:>8.3f}  "
              f"{m['ghost_ratio']*100:>7.2f}%  "
              f"{m['hole_ratio']*100:>7.2f}%  "
              f"{m['area_err']:>10.3f}")


def print_comparison_table(base, current, verbose=False):
    header = (f"{'Scenario':<12} {'Metric':<12} "
              f"{'Baseline':>10}  {'Current':>10}  {'Delta':>10}  {'Status':>8}")
    print(header)
    print("-" * len(header))

    for name in base:
        if name not in current:
            continue
        rows_printed = 0
        for metric in ["mae", "ghost_ratio", "hole_ratio", "area_err"]:
            b = base[name][metric]
            c = current[name][metric]
            delta = c - b
            tol = TOLERANCES[metric]
            threshold = max(tol["abs_tol"], b * tol["rel_tol"])
            status = "FAIL" if delta > threshold else "OK"

            # verbose 模式顯示所有；非 verbose 只顯示 FAIL + 每情境第一行（顯示 scenario 名稱）
            if verbose or status == "FAIL" or rows_printed == 0:
                scenario_label = name if rows_printed == 0 else ""
                print(f"{scenario_label:<12} {metric:<12} "
                      f"{b:>10.3f}  {c:>10.3f}  {delta:>+10.3f}  {status:>8}")
                rows_printed += 1
        if rows_printed > 0:
            print()


# ============================================================
#  CLI
# ============================================================

def main():
    ap = argparse.ArgumentParser(description="TCF regression test")
    ap.add_argument("--freeze", action="store_true",
                    help="凍結當前結果作為 baseline（覆寫既有 baseline）")
    ap.add_argument("--verbose", "-v", action="store_true",
                    help="顯示所有指標（非 verbose 模式只顯示失敗）")
    args = ap.parse_args()

    if args.freeze:
        freeze()
        return 0

    return check(verbose=args.verbose)


if __name__ == "__main__":
    sys.exit(main())
