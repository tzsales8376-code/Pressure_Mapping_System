"""
兩套 spatial filtering algorithms：
  Legacy — 模擬目前 Pressure_Mapping_System 的演算法
           (erosion + multi-pass inpainting + gaussian)
  TCF    — Temporal-Coherence Filter（新提案）
           利用最近 N 幀維護每點的穩定性指標，
           用 confidence 動態調整 erosion 與 inpainting 強度

兩個都是 frame-by-frame streaming 介面，方便逐幀比較。
"""
import numpy as np
from scipy.ndimage import gaussian_filter

ROWS, COLS = 40, 40


# ============================================================
# LEGACY — 模擬現有 Pressure_Mapping_System 的 pipeline
# ============================================================
class LegacyFilter:
    """
    現有演算法（MainViewModel.cs 中 SpatialSignalProcess + 串擾濾波）
    參數 noise_filter_pct 對應 Signal Purity 滑桿 (0~20)。
    """
    def __init__(self, noise_filter_pct=5.0, noise_floor=5.0):
        self.nf_pct = noise_filter_pct
        self.noise_floor = noise_floor

    def process(self, frame):
        out = frame.copy()

        # 1. 全域門檻
        out[out < self.noise_floor] = 0

        # 2. Stage 1 + Stage 2 串擾濾波（若啟用）
        if self.nf_pct > 0:
            peak = out.max()
            if peak > 0:
                # Stage 1：增強全域門檻
                dyn_thresh = peak * (self.nf_pct / 100.0) * 3.0
                out[(out > 0) & (out < dyn_thresh)] = 0

            # Stage 2：邊緣侵蝕
            max_passes = max(1, int(self.nf_pct * 1.5))
            erosion_ratio = 0.35
            for _ in range(max_passes):
                snap = out.copy()
                any_eroded = False
                for r in range(ROWS):
                    for c in range(COLS):
                        if snap[r, c] <= 0:
                            continue
                        is_bdry = False
                        max_neighbor = 0
                        for dr in range(-2, 3):
                            for dc in range(-2, 3):
                                if dr == 0 and dc == 0:
                                    continue
                                nr, nc = r + dr, c + dc
                                if not (0 <= nr < ROWS and 0 <= nc < COLS):
                                    if abs(dr) <= 1 and abs(dc) <= 1:
                                        is_bdry = True
                                    continue
                                v = snap[nr, nc]
                                if v <= 0:
                                    if abs(dr) <= 1 and abs(dc) <= 1:
                                        is_bdry = True
                                elif v > max_neighbor:
                                    max_neighbor = v
                        if is_bdry and max_neighbor > 0 and snap[r, c] < max_neighbor * erosion_ratio:
                            out[r, c] = 0
                            any_eroded = True
                if not any_eroded:
                    break

        # 3. SpatialSignalProcess: 清除孤立雜訊
        snap = out.copy()
        for r in range(ROWS):
            for c in range(COLS):
                if snap[r, c] <= 0:
                    continue
                ac = 0
                for dr in range(-1, 2):
                    for dc in range(-1, 2):
                        if dr == 0 and dc == 0: continue
                        nr, nc = r + dr, c + dc
                        if 0 <= nr < ROWS and 0 <= nc < COLS and snap[nr, nc] > 0:
                            ac += 1
                if ac <= 1:
                    wide = 0
                    for dr in range(-2, 3):
                        for dc in range(-2, 3):
                            if dr == 0 and dc == 0: continue
                            nr, nc = r + dr, c + dc
                            if 0 <= nr < ROWS and 0 <= nc < COLS and snap[nr, nc] > 0:
                                wide += 1
                    if wide <= 3:
                        out[r, c] = 0

        # 4. Inpainting：多輪迭代填補空洞
        for _ in range(10):
            snap = out.copy()
            any_filled = False
            for r in range(ROWS):
                for c in range(COLS):
                    if snap[r, c] > 0:
                        continue
                    ac, asum = 0, 0.0
                    for dr in range(-1, 2):
                        for dc in range(-1, 2):
                            if dr == 0 and dc == 0: continue
                            nr, nc = r + dr, c + dc
                            if 0 <= nr < ROWS and 0 <= nc < COLS and snap[nr, nc] > 0:
                                ac += 1
                                asum += snap[nr, nc]
                    if ac >= 2:
                        out[r, c] = asum / ac
                        any_filled = True
            if not any_filled:
                break

        # 5. 四方向強制填補（Step 2b）
        for _ in range(3):
            snap = out.copy()
            any_filled = False
            for r in range(ROWS):
                for c in range(COLS):
                    if snap[r, c] > 0:
                        continue
                    # 四方向搜索半徑 5
                    dirs, nsum, ncount = 0, 0.0, 0
                    for d in range(1, 6):
                        if r - d >= 0 and snap[r - d, c] > 0:
                            dirs += 1; nsum += snap[r - d, c]; ncount += 1; break
                    for d in range(1, 6):
                        if r + d < ROWS and snap[r + d, c] > 0:
                            dirs += 1; nsum += snap[r + d, c]; ncount += 1; break
                    for d in range(1, 6):
                        if c - d >= 0 and snap[r, c - d] > 0:
                            dirs += 1; nsum += snap[r, c - d]; ncount += 1; break
                    for d in range(1, 6):
                        if c + d < COLS and snap[r, c + d] > 0:
                            dirs += 1; nsum += snap[r, c + d]; ncount += 1; break
                    if dirs >= 3 and ncount > 0:
                        out[r, c] = nsum / ncount
                        any_filled = True
            if not any_filled:
                break

        # 6. Gaussian 平滑
        out = gaussian_filter(out, sigma=0.8)

        return out


# ============================================================
# TCF — Temporal-Coherence Filter
# ============================================================
class TcfFilter:
    """
    時間一致性濾波器。

    核心：每個感測點維護最近 N 幀的 μ, σ，用來計算 temporal confidence。

      μ_short  = 最近 N 幀的平均
      σ_short  = 最近 N 幀的標準差
      stability = μ / (σ + ε)     — 高壓低變異 = 高穩定性
      transient = σ                — 大波動 = 過渡訊號

    處理流程（單幀進來時）：
      Step 1: 更新 ring buffer
      Step 2: 從 buffer 計算每點的 μ_short, σ_short
      Step 3: 用 confidence 引導三種動作：
              - Trust（穩定點）→ 輸出 μ_short（抑制當前雜訊）
              - Track（瞬變點）→ 輸出 current frame（避免滯留）
              - Suspect（低值變動）→ 侵蝕掉
              - Fill-hole（受壓區內死點）→ 從鄰居填補，但僅當鄰居是 Trust 時

    關鍵設計：
      - N 可配置：60 FPS 下 N=5 ≈ 83ms（文案提過的響應時間）
      - 所有判定都是連續變數，沒有硬開關
      - 不需要使用者在「靜態 / 動態」間切換
    """
    def __init__(self, fps=60, response_ms=80, noise_floor=5.0,
                 stability_threshold=15.0,
                 erosion_ratio_base=0.35):
        # N = 響應時間對應的幀數
        self.n_history = max(2, int(fps * response_ms / 1000.0))
        self.noise_floor = noise_floor
        self.stability_threshold = stability_threshold
        self.erosion_ratio_base = erosion_ratio_base

        self.history = []  # ring buffer of last N frames

    def reset(self):
        self.history = []

    def process(self, frame):
        # Step 0: 基本門檻（跟 legacy 一樣）
        f = frame.copy()
        f[f < self.noise_floor] = 0

        # Step 1: 加入 history
        self.history.append(f.copy())
        if len(self.history) > self.n_history:
            self.history.pop(0)

        # Step 2: 計算 μ, σ
        hist = np.array(self.history)  # shape (N, ROWS, COLS)
        mu = hist.mean(axis=0)
        sigma = hist.std(axis=0)

        # stability = μ / (σ + ε)
        eps = 1.0
        stability = mu / (sigma + eps)

        # confidence（0~1）基於 stability 的 sigmoid-like 映射
        # stability 高（穩定壓力）→ confidence ~ 1
        # stability 低（雜訊或過渡）→ confidence ~ 0
        confidence = stability / (stability + self.stability_threshold)
        # shape (ROWS, COLS)

        # Step 3: 輸出值 = 信任加權混合 μ 與當前幀
        # high confidence → 用 μ（時間平滑降低雜訊、保留穩定結構）
        # low confidence → 用 current frame（讓動態訊號即時反映）
        out = confidence * mu + (1 - confidence) * f

        # Step 4: 邊界侵蝕，但只侵蝕「鄰居也不穩定」的邊界點
        # 避免把「有一個穩定鄰居的靜態邊緣」也咬掉
        peak = out.max()
        if peak > 0:
            for r in range(ROWS):
                for c in range(COLS):
                    if out[r, c] <= 0:
                        continue
                    # 找鄰居中最強的 "stable" 點
                    max_stable_neighbor = 0
                    has_zero_neighbor = False
                    for dr in range(-2, 3):
                        for dc in range(-2, 3):
                            if dr == 0 and dc == 0:
                                continue
                            nr, nc = r + dr, c + dc
                            if not (0 <= nr < ROWS and 0 <= nc < COLS):
                                if abs(dr) <= 1 and abs(dc) <= 1:
                                    has_zero_neighbor = True
                                continue
                            if out[nr, nc] <= 0:
                                if abs(dr) <= 1 and abs(dc) <= 1:
                                    has_zero_neighbor = True
                                continue
                            # 只有當鄰居有穩定性才算
                            neighbor_stable_val = out[nr, nc] * confidence[nr, nc]
                            if neighbor_stable_val > max_stable_neighbor:
                                max_stable_neighbor = neighbor_stable_val

                    # 只有邊界點（有 0 鄰居）且自己不穩定、鄰居也不強 → 侵蝕
                    if has_zero_neighbor and confidence[r, c] < 0.3:
                        if max_stable_neighbor == 0 or out[r, c] < max_stable_neighbor * self.erosion_ratio_base:
                            out[r, c] = 0

        # Step 5: Inpainting — 只填「鄰居都是穩定點」的空洞
        # 穩定鄰居 ≥ 3 才填，避免填入過渡訊號殘影
        for _ in range(5):
            snap = out.copy()
            confidence_snap = confidence.copy()
            any_filled = False
            for r in range(ROWS):
                for c in range(COLS):
                    if snap[r, c] > 0:
                        continue
                    stable_neighbors = 0
                    nsum, ncount = 0.0, 0
                    for dr in range(-1, 2):
                        for dc in range(-1, 2):
                            if dr == 0 and dc == 0:
                                continue
                            nr, nc = r + dr, c + dc
                            if 0 <= nr < ROWS and 0 <= nc < COLS:
                                if snap[nr, nc] > 0 and confidence_snap[nr, nc] > 0.5:
                                    stable_neighbors += 1
                                    nsum += snap[nr, nc]
                                    ncount += 1
                    # 至少 3 個穩定鄰居，且此位置在過去有壓力存在過（mu > threshold）
                    # → 這是受壓區內的死點，值得填補
                    if stable_neighbors >= 3 and mu[r, c] > self.noise_floor:
                        out[r, c] = nsum / ncount
                        any_filled = True
            if not any_filled:
                break

        # Step 6: 輕度 Gaussian 平滑
        out = gaussian_filter(out, sigma=0.6)
        out[out < self.noise_floor] = 0  # 清掉平滑後的殘餘

        return out


# ============================================================
# 評估函數：計算處理結果與 ground truth 的距離
# ============================================================
def metrics(gt, est):
    """
    四個指標：
      - MAE: mean absolute error (整體誤差)
      - Peak error: |peak_est - peak_gt| / peak_gt (峰值誤差)
      - Area error: |active_est - active_gt| / (active_gt + 1) (活躍區面積誤差)
      - Ghost ratio: 在 gt=0 的地方 est > threshold 的比例 (串擾殘影)
    """
    mae = np.abs(gt - est).mean()
    peak_gt = gt.max()
    peak_est = est.max()
    peak_err = abs(peak_est - peak_gt) / (peak_gt + 1e-9) if peak_gt > 0 else 0

    active_gt = (gt > 5).sum()
    active_est = (est > 5).sum()
    area_err = abs(active_est - active_gt) / (active_gt + 1) if active_gt > 0 else 0

    # ghost: 真實沒壓力但演算法說有
    ghost_mask = (gt < 1) & (est > 5)
    ghost_ratio = ghost_mask.sum() / max(1, (gt < 1).sum())

    # hole: 真實有壓力但演算法說沒有
    hole_mask = (gt > 20) & (est < 5)
    hole_ratio = hole_mask.sum() / max(1, (gt > 20).sum())

    return {
        'mae': mae,
        'peak_err': peak_err,
        'area_err': area_err,
        'ghost_ratio': ghost_ratio,
        'hole_ratio': hole_ratio,
    }


if __name__ == "__main__":
    import tcf_scenarios as sc

    print("Quick smoke test:")
    gt, obs = sc.scenario_static(n_frames=30)
    leg = LegacyFilter(noise_filter_pct=5.0)
    tcf = TcfFilter(fps=60, response_ms=80)

    # 只看最後一幀（buffer 填滿）
    for t in range(30):
        leg_out = leg.process(obs[t])
        tcf_out = tcf.process(obs[t])

    print(f"  static frame 29:")
    print(f"    gt peak={gt[29].max():.1f}, active={(gt[29]>5).sum()}")
    print(f"    obs peak={obs[29].max():.1f}, active={(obs[29]>5).sum()}")
    print(f"    legacy peak={leg_out.max():.1f}, active={(leg_out>5).sum()}, metrics={metrics(gt[29], leg_out)}")
    print(f"    tcf peak={tcf_out.max():.1f}, active={(tcf_out>5).sum()}, metrics={metrics(gt[29], tcf_out)}")
