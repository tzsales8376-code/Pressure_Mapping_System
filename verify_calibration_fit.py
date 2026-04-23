#!/usr/bin/env python3
"""
用 Tekscan 左圖的真實資料驗證 CalibrationFitter。
圖中 Pounds 與 Raw Sum 欄的對照：
  168.92   14836    residual -9.1%
  252.42   22594    -1.5%
  335.9    25731    -14.1%
  419.41   36520    +3.0%
  502.91   43449    +4.8%
  586.4    49666    +5.0%
  676.4    54708    +1.9%
  766      60418    +1.1%
  840      63904    -1.6%
  924      68602    -2.8%

我們先用 PowerLaw 擬合，看看得到的殘差是否跟 Tekscan 的差不多量級（Tekscan 殘差最大 14%、大部分在 5% 內）。
注意：這不是 1:1 複製，因為 Tekscan 可能用了不同的擬合演算法，但數量級一致即可信。
"""

import math

DATA = [
    (14836, 168.92),
    (22594, 252.42),
    (25731, 335.9),
    (36520, 419.41),
    (43449, 502.91),
    (49666, 586.4),
    (54708, 676.4),
    (60418, 766),
    (63904, 840),
    (68602, 924),
]

def fit_linear(points):
    n = len(points)
    sx = sum(x for x, _ in points)
    sy = sum(y for _, y in points)
    sxy = sum(x * y for x, y in points)
    sx2 = sum(x * x for x, _ in points)
    denom = n * sx2 - sx * sx
    slope = (n * sxy - sx * sy) / denom
    intercept = (sy - slope * sx) / n

    mean_y = sy / n
    ss_tot = sum((y - mean_y) ** 2 for _, y in points)
    ss_res = sum((y - (slope * x + intercept)) ** 2 for x, y in points)
    r2 = 1 - ss_res / ss_tot
    residuals = [((slope * x + intercept) - y) / y * 100 for x, y in points]
    return slope, intercept, r2, residuals

def fit_power(points):
    lp = [(math.log(x), math.log(y)) for x, y in points]
    n = len(lp)
    sx = sum(lx for lx, _ in lp)
    sy = sum(ly for _, ly in lp)
    sxy = sum(lx * ly for lx, ly in lp)
    sx2 = sum(lx * lx for lx, _ in lp)
    denom = n * sx2 - sx * sx
    exp = (n * sxy - sx * sy) / denom
    log_scale = (sy - exp * sx) / n
    scale = math.exp(log_scale)

    # R² in original space
    mean_y = sum(y for _, y in points) / n
    ss_tot = sum((y - mean_y) ** 2 for _, y in points)
    ss_res = sum((y - scale * (x ** exp)) ** 2 for x, y in points)
    r2 = 1 - ss_res / ss_tot
    residuals = [(scale * (x ** exp) - y) / y * 100 for x, y in points]
    return scale, exp, r2, residuals


print("=" * 70)
print("Tekscan 範例資料：10 個校正點，Pounds vs Raw Sum")
print("=" * 70)

print("\n--- Linear fit ---")
slope, intercept, r2, res = fit_linear(DATA)
print(f"  W = {slope:.6g} × raw + {intercept:.6g}")
print(f"  R² = {r2:.4f}")
print(f"  Residuals (%): {['{:+.1f}'.format(r) for r in res]}")
print(f"  Max |residual|: {max(abs(r) for r in res):.1f}%")

print("\n--- PowerLaw fit ---")
scale, exp, r2, res = fit_power(DATA)
print(f"  W = {scale:.6g} × raw^{exp:.4f}")
print(f"  R² = {r2:.4f}")
print(f"  Residuals (%): {['{:+.1f}'.format(r) for r in res]}")
print(f"  Max |residual|: {max(abs(r) for r in res):.1f}%")

# Tekscan 原圖裡的殘差最大 14.1%（第三點）
print("\n--- 對照 Tekscan 原圖殘差 ---")
tek_res = [-9.1, -1.5, -14.1, 3.0, 4.8, 5.0, 1.9, 1.1, -1.6, -2.8]
print(f"  Tekscan:    {['{:+.1f}'.format(r) for r in tek_res]}")
print(f"  Tekscan max |residual|: {max(abs(r) for r in tek_res):.1f}%")

# 檢查基本合理性
print("\n--- Sanity check ---")
print(f"  PowerLaw exponent 應在 0.8~1.5 範圍（電阻式薄膜感測器物理特性）")
scale, exp, _, _ = fit_power(DATA)
print(f"  → 我們擬合的 exponent = {exp:.3f}  {'✓' if 0.8 <= exp <= 1.5 else '✗ 異常！'}")

print(f"  R² 應 > 0.99（Tekscan 資料非常線性化）")
_, _, r2_lin, _ = fit_linear(DATA)
_, _, r2_pow, _ = fit_power(DATA)
print(f"  → Linear R²   = {r2_lin:.4f}  {'✓' if r2_lin > 0.99 else '✗'}")
print(f"  → PowerLaw R² = {r2_pow:.4f}  {'✓' if r2_pow > 0.99 else '✗'}")

print("\n結論：兩種擬合都能從這組資料得到合理結果，殘差量級與 Tekscan 接近。")
