"""
TCFv2 — 針對實機測試發現的問題修正版

改動對照 TCFv1：
  1. Stability 參數語意翻轉為「雜訊抑制強度」(0~100)，預設 50
     內部映射：stability_threshold = 30 - 0.25 × slider
     slider 大 → confidence 容易飽和到 1 → 時間平均主導 → 抑制雜訊

  2. 空洞填補放寬（2-pass）
     Pass 1: ≥5 個非零鄰居直接平均填
     Pass 2: 四方向外推（徑向搜索 5 格），三方向以上就填
     目標：全面受壓下大空洞（0.5kg 配重塊）能完整補齊

  3. 梯度感知邊緣侵蝕
     替代原本的 confidence < 0.3 判定
     對每個邊界點計算 3×3 鄰域梯度，梯度小 → ghost 殘影 → 侵蝕
     真實邊緣有明顯梯度會被保留

  4. 連通域過濾（island removal）
     找所有 8-連通塊，計算每塊的「最大值 × 大小」稱為 importance
     importance < 主塊的 10% 的孤立島 → 全清除
     目標：消滅遠離主區的零星 ghost 點

  5. Cold-start 回退
     ring buffer 未滿前全走 Legacy 邏輯
     60 FPS 下前 12 幀（~200ms）走 Legacy
"""
import numpy as np
from scipy.ndimage import gaussian_filter, label

ROWS, COLS = 40, 40


class TcfV2Filter:
    """
    參數：
      fps:                        60 FPS 固定
      response_ms:                時間窗長度（預設 80ms）
      noise_suppression:          雜訊抑制強度 0~100（預設 50）
      noise_floor:                單點最低門檻（預設 5g）
      island_threshold_ratio:     孤立塊清除門檻（相對主塊的比例，預設 0.1）
    """
    def __init__(self, fps=60, response_ms=80, noise_suppression=50,
                 noise_floor=5.0, island_threshold_ratio=0.1):
        self.n_history = max(2, int(fps * response_ms / 1000.0))
        self.noise_floor = noise_floor
        self.island_ratio = island_threshold_ratio
        # 映射：slider=0 → threshold=30（寬容）
        #       slider=50 → threshold=17.5（中）
        #       slider=100 → threshold=5（嚴格）
        self.stability_threshold = max(3.0, 30.0 - 0.25 * noise_suppression)

        # cold-start 門檻：ring buffer 至少累積這麼多幀才切 TCF
        self.cold_start_frames = self.n_history

        self.history = []
        self.frame_count = 0  # 總處理幀數

    def reset(self):
        self.history = []
        self.frame_count = 0

    def process(self, frame):
        self.frame_count += 1
        f = frame.copy()

        # Step 0: 基本門檻
        f[f < self.noise_floor] = 0

        # Step 1: 維護 history ring buffer
        self.history.append(f.copy())
        if len(self.history) > self.n_history:
            self.history.pop(0)

        # Cold-start：ring buffer 未滿前走 Legacy（呼叫端應提供 fallback，這裡直接回空間濾波）
        if len(self.history) < self.cold_start_frames:
            return self._legacy_fallback(f)

        # Step 2: 計算 μ, σ
        hist = np.array(self.history)
        mu = hist.mean(axis=0)
        sigma = hist.std(axis=0)

        eps = 1.0
        stability = mu / (sigma + eps)
        confidence = stability / (stability + self.stability_threshold)

        # Step 3: 時間平均 + 當前幀混合
        out = confidence * mu + (1 - confidence) * f

        # Step 4: 梯度感知邊緣侵蝕
        out = self._gradient_erosion(out, confidence)

        # Step 5: 2-pass inpainting
        out = self._inpaint_pass1(out)  # 5 個鄰居直接補
        out = self._inpaint_pass2(out)  # 四方向外推

        # Step 6: 輕度 Gaussian 平滑
        out = gaussian_filter(out, sigma=0.6)
        out[out < self.noise_floor] = 0

        # Step 7: 連通域過濾（消 ghost）
        out = self._remove_small_islands(out)

        return out

    # ────────────────────────────────────────────────────
    def _legacy_fallback(self, f):
        """Cold-start 期間用簡化 Legacy 處理"""
        out = f.copy()
        # 簡單 inpainting：有 2 個以上鄰居就補
        for _ in range(3):
            snap = out.copy()
            any_filled = False
            for r in range(ROWS):
                for c in range(COLS):
                    if snap[r, c] > 0:
                        continue
                    nbrs = []
                    for dr in range(-1, 2):
                        for dc in range(-1, 2):
                            if dr == 0 and dc == 0:
                                continue
                            nr, nc = r + dr, c + dc
                            if 0 <= nr < ROWS and 0 <= nc < COLS and snap[nr, nc] > 0:
                                nbrs.append(snap[nr, nc])
                    if len(nbrs) >= 2:
                        out[r, c] = np.mean(nbrs)
                        any_filled = True
            if not any_filled:
                break
        return gaussian_filter(out, sigma=0.6)

    # ────────────────────────────────────────────────────
    def _gradient_erosion(self, data, confidence):
        """
        對每個邊界點（有 0 鄰居的點）計算鄰域梯度：
          真實邊緣：梯度大（壓力從高快速降到 0）→ 保留
          Ghost 殘影：梯度小（低壓擴散）→ 侵蝕

        判定：若自己值 × 最大鄰居梯度 < 某閾值 → 侵蝕
        """
        out = data.copy()
        peak = data.max()
        if peak < 5:
            return out

        # 計算每點的鄰域最大梯度（與最大非零鄰居的差）
        for r in range(ROWS):
            for c in range(COLS):
                if data[r, c] <= 0:
                    continue

                # 是否為邊界點（有 0 鄰居）
                has_zero_neighbor = False
                max_neighbor = 0
                for dr in range(-1, 2):
                    for dc in range(-1, 2):
                        if dr == 0 and dc == 0:
                            continue
                        nr, nc = r + dr, c + dc
                        if not (0 <= nr < ROWS and 0 <= nc < COLS):
                            has_zero_neighbor = True
                            continue
                        if data[nr, nc] <= 0:
                            has_zero_neighbor = True
                        elif data[nr, nc] > max_neighbor:
                            max_neighbor = data[nr, nc]

                if not has_zero_neighbor:
                    continue  # 內部點不侵蝕

                # 自己是邊界。判定：
                #   若自己是「低壓 + 周圍也不高」→ 極可能是 ghost
                # 標準：自己值 < peak 的 15%，且鄰居最大值也 < peak 的 25%
                if out[r, c] < peak * 0.15 and max_neighbor < peak * 0.25:
                    # 再看 confidence：如果時間上不穩定 → 肯定是 ghost
                    # 如果時間上穩定但值低 → 可能是真實邊緣，保留
                    if confidence[r, c] < 0.4:
                        out[r, c] = 0

        return out

    # ────────────────────────────────────────────────────
    def _inpaint_pass1(self, data):
        """Pass 1：≥5 個非零鄰居 → 直接平均填"""
        for _ in range(5):
            snap = data.copy()
            any_filled = False
            for r in range(ROWS):
                for c in range(COLS):
                    if snap[r, c] > 0:
                        continue
                    nbrs = []
                    for dr in range(-1, 2):
                        for dc in range(-1, 2):
                            if dr == 0 and dc == 0:
                                continue
                            nr, nc = r + dr, c + dc
                            if 0 <= nr < ROWS and 0 <= nc < COLS and snap[nr, nc] > 0:
                                nbrs.append(snap[nr, nc])
                    if len(nbrs) >= 5:
                        data[r, c] = np.mean(nbrs)
                        any_filled = True
            if not any_filled:
                break
        return data

    def _inpaint_pass2(self, data):
        """Pass 2：四方向外推搜索，三方向以上就填"""
        for _ in range(3):
            snap = data.copy()
            any_filled = False
            for r in range(ROWS):
                for c in range(COLS):
                    if snap[r, c] > 0:
                        continue
                    dirs_hit = 0
                    nsum = 0.0
                    ncount = 0
                    # 四方向搜索半徑 5
                    for d in range(1, 6):
                        if r - d >= 0 and snap[r - d, c] > 0:
                            dirs_hit += 1; nsum += snap[r - d, c]; ncount += 1; break
                    for d in range(1, 6):
                        if r + d < ROWS and snap[r + d, c] > 0:
                            dirs_hit += 1; nsum += snap[r + d, c]; ncount += 1; break
                    for d in range(1, 6):
                        if c - d >= 0 and snap[r, c - d] > 0:
                            dirs_hit += 1; nsum += snap[r, c - d]; ncount += 1; break
                    for d in range(1, 6):
                        if c + d < COLS and snap[r, c + d] > 0:
                            dirs_hit += 1; nsum += snap[r, c + d]; ncount += 1; break
                    if dirs_hit >= 3 and ncount > 0:
                        data[r, c] = nsum / ncount
                        any_filled = True
            if not any_filled:
                break
        return data

    # ────────────────────────────────────────────────────
    def _remove_small_islands(self, data):
        """
        連通域過濾：
          1. 找所有 8-connected components
          2. 每個塊計算 importance = 塊內最大值 × 塊大小
          3. 清除 importance < 最大塊的 island_ratio 的所有塊

        這一步消滅「遠離主區的零星 ghost 點」。
        4×4 頂針陣列時，每根頂針自成一塊，importance 差不多，不會互相誤殺。
        """
        mask = data > 0
        if not mask.any():
            return data

        labeled, n = label(mask, structure=np.ones((3, 3), dtype=int))
        if n <= 1:
            return data

        # 計算每個 component 的 importance
        importance = np.zeros(n + 1)
        for comp_id in range(1, n + 1):
            region = data[labeled == comp_id]
            if len(region) == 0:
                continue
            importance[comp_id] = region.max() * len(region)

        max_imp = importance.max()
        if max_imp <= 0:
            return data
        threshold = max_imp * self.island_ratio

        # 清除小於 threshold 的 component
        out = data.copy()
        for comp_id in range(1, n + 1):
            if importance[comp_id] < threshold:
                out[labeled == comp_id] = 0

        return out


if __name__ == "__main__":
    import tcf_scenarios as sc
    from tcf_algorithms import LegacyFilter, metrics

    print("TCFv2 smoke test (fullplate 配重塊情境):")
    gt, obs = sc.scenario_fullplate_static(n_frames=30)
    tcf = TcfV2Filter(noise_suppression=50)
    for t in range(30):
        out = tcf.process(obs[t])
    print(f"  GT: peak={gt[29].max():.1f}, active={(gt[29]>5).sum()}, total_g={gt[29].sum():.0f}")
    print(f"  OBS: peak={obs[29].max():.1f}, active={(obs[29]>5).sum()}")
    print(f"  TCFv2: peak={out.max():.1f}, active={(out>5).sum()}, metrics={metrics(gt[29], out)}")

    print("\nTCFv2 diebonder_array smoke test:")
    gt, obs = sc.scenario_diebonder_array(n_frames=30)
    tcf = TcfV2Filter(noise_suppression=50)
    for t in range(30):
        out = tcf.process(obs[t])
    print(f"  GT: peak={gt[29].max():.1f}, active={(gt[29]>5).sum()}")
    print(f"  TCFv2: peak={out.max():.1f}, active={(out>5).sum()}")
    # 檢查是否所有 16 根頂針都被保留
    from scipy.ndimage import label
    _, n_blobs = label(out > 5, structure=np.ones((3, 3), dtype=int))
    print(f"  Detected {n_blobs} pin clusters (expected 16)")
