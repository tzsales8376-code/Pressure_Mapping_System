"""
Pressure scenario generators — 產生 4 種測試情境的「真實壓力」序列與「感測器觀測」序列。

每個情境回傳 (T, 40, 40) 的陣列：
  - gt_series: ground truth pressure (真實應該是什麼)
  - obs_series: 感測器觀測（加入死點、機械串擾、雜訊）

目的：讓後續演算法在相同輸入下比較，看誰更接近 gt。
"""
import numpy as np

ROWS, COLS = 40, 40
np.random.seed(42)


def gaussian_blob(cy, cx, peak, sigma, rows=ROWS, cols=COLS):
    """在 (cy, cx) 產生高斯壓力點"""
    yy, xx = np.mgrid[:rows, :cols]
    g = peak * np.exp(-((yy - cy) ** 2 + (xx - cx) ** 2) / (2 * sigma ** 2))
    return g


def add_sensor_artifacts(gt, dead_points=None, frame_idx=0, is_transient=False):
    """
    模擬感測器效應：
    - dead_points: 指定的死點座標，這些點回傳 0（感測器故障）
    - 機械串擾：壓力區周圍 2~3 格產生 真實峰值 15% 的串擾低壓
    - 時變雜訊：背景高斯雜訊 σ=2g

    is_transient=True 代表這幀是過渡狀態（剛壓下或剛釋放），
    串擾會更嚴重（機械結構還在變形）
    """
    obs = gt.copy()
    peak = obs.max()

    # 1. 機械串擾：對每個活躍區的邊緣放射出殘影
    if peak > 5:
        active = gt > peak * 0.1
        # 用 binary dilation 擴張 2~3 格
        from scipy.ndimage import binary_dilation, gaussian_filter
        dilated = binary_dilation(active, iterations=3)
        crosstalk_region = dilated & ~active  # 擴張出來、但原本沒壓力的區域
        # 串擾強度 = peak 的 10~18%，過渡時更高
        crosstalk_level = peak * (0.18 if is_transient else 0.12)
        # 加上空間漸變（越遠越弱）
        dist = gaussian_filter(active.astype(float), sigma=1.5)
        obs = np.where(crosstalk_region,
                       crosstalk_level * dist / (dist.max() + 1e-9),
                       obs)

    # 2. 死點：指定位置強制為 0
    if dead_points is not None:
        for (dr, dc) in dead_points:
            if 0 <= dr < ROWS and 0 <= dc < COLS:
                obs[dr, dc] = 0

    # 3. 時變背景雜訊
    noise = np.random.normal(0, 2.0, obs.shape)
    # 在原本無壓力區也加微量雜訊
    obs = obs + noise
    obs = np.clip(obs, 0, None)

    return obs


# ============================================================
# Scenario 1：純靜態
# ============================================================
def scenario_static(n_frames=60):
    """一個穩定的壓力印子，中心 (20, 20)，有幾個死點"""
    gt_series = []
    obs_series = []
    # 固定死點：模擬感測器製造瑕疵
    dead_points = [(19, 20), (20, 21), (21, 19), (20, 20)]

    for t in range(n_frames):
        # 壓力峰值有非常輕微的抖動（真實情況）
        peak = 150 + np.random.normal(0, 2)
        gt = gaussian_blob(20, 20, peak, 4)
        obs = add_sensor_artifacts(gt, dead_points=dead_points,
                                   frame_idx=t, is_transient=False)
        gt_series.append(gt)
        obs_series.append(obs)
    return np.array(gt_series), np.array(obs_series)


# ============================================================
# Scenario 2：純動態
# ============================================================
def scenario_dynamic(n_frames=60):
    """壓力印子從左上移到右下，一路都在動"""
    gt_series = []
    obs_series = []

    for t in range(n_frames):
        # 位置隨時間從 (10, 10) 移到 (30, 30)
        progress = t / (n_frames - 1)
        cy = 10 + progress * 20
        cx = 10 + progress * 20
        gt = gaussian_blob(cy, cx, 150, 4)
        # 動態時沒有固定死點（手指位置在變）
        obs = add_sensor_artifacts(gt, dead_points=None,
                                   frame_idx=t, is_transient=True)
        gt_series.append(gt)
        obs_series.append(obs)
    return np.array(gt_series), np.array(obs_series)


# ============================================================
# Scenario 3：Die Bonder 頂 Pin — 壓下 → 靜止 → 釋放
# ============================================================
def scenario_diebonder(n_frames=90):
    """
    0~20 幀：無壓力
    20~30 幀：快速壓下（過渡期，串擾嚴重）
    30~70 幀：穩定施壓（靜態）
    70~80 幀：釋放
    80~90 幀：無壓力
    """
    gt_series = []
    obs_series = []
    dead_points = [(19, 20), (20, 21)]  # 固定死點

    for t in range(n_frames):
        if t < 20:
            gt = np.zeros((ROWS, COLS))
            is_transient = False
        elif t < 30:
            # 壓下過程，強度從 0 上升到 150
            progress = (t - 20) / 10.0
            peak = 150 * progress
            gt = gaussian_blob(20, 20, peak, 4)
            is_transient = True
        elif t < 70:
            # 穩定
            peak = 150 + np.random.normal(0, 1.5)
            gt = gaussian_blob(20, 20, peak, 4)
            is_transient = False
        elif t < 80:
            # 釋放過程
            progress = 1.0 - (t - 70) / 10.0
            peak = 150 * progress
            gt = gaussian_blob(20, 20, peak, 4)
            is_transient = True
        else:
            gt = np.zeros((ROWS, COLS))
            is_transient = False

        obs = add_sensor_artifacts(gt, dead_points=dead_points,
                                   frame_idx=t, is_transient=is_transient)
        gt_series.append(gt)
        obs_series.append(obs)
    return np.array(gt_series), np.array(obs_series)


# ============================================================
# Scenario 4：滑動 → 停頓 → 滑走（混合）
# ============================================================
def scenario_mixed(n_frames=90):
    """
    0~30：滑動  (10, 10) → (20, 20)
    30~60：停在 (20, 20) 穩定
    60~90：滑走 (20, 20) → (30, 30)
    """
    gt_series = []
    obs_series = []
    dead_points = [(19, 20), (20, 21)]

    for t in range(n_frames):
        if t < 30:
            progress = t / 30.0
            cy = 10 + progress * 10
            cx = 10 + progress * 10
            is_transient = True
            dp = None  # 滑動時死點會移動，簡化假設沒有
        elif t < 60:
            cy, cx = 20, 20
            is_transient = False
            dp = dead_points  # 停在 20,20 時，20,20 附近的死點才會顯現
        else:
            progress = (t - 60) / 30.0
            cy = 20 + progress * 10
            cx = 20 + progress * 10
            is_transient = True
            dp = None

        gt = gaussian_blob(cy, cx, 150, 4)
        obs = add_sensor_artifacts(gt, dead_points=dp,
                                   frame_idx=t, is_transient=is_transient)
        gt_series.append(gt)
        obs_series.append(obs)
    return np.array(gt_series), np.array(obs_series)


# ============================================================
# Scenario 5：0.5kg 配重塊全面靜態 — 貼近你圖 1 的實機情況
# ============================================================
def scenario_fullplate_static(n_frames=60):
    """
    模擬 0.5kg (~500g) 配重塊放在 40×40mm 感測器上。
    - GT：接近平坦、全面覆蓋的壓力場（500g / 1600 points ≈ 0.31 g/point）
      但實際測試中重量會略集中在接觸介面，呈現中心略高的鈍高斯（σ=10 大範圍）
    - 死點：模擬薄膜感測器的製造瑕疵（10~20 個隨機分佈的死點）
    - 機械串擾：全面受壓時串擾效應不明顯（沒有銳利邊緣）
    """
    gt_series = []
    obs_series = []
    # 預先決定死點位置（整個量測期間固定）— 模擬薄膜製造瑕疵
    rng = np.random.RandomState(2026)
    dead_count = 18
    dead_points = [(rng.randint(5, 35), rng.randint(5, 35)) for _ in range(dead_count)]
    # 特別在中心區造一個大空洞（模擬你圖 1 左上的大空洞）
    for dr in range(-2, 3):
        for dc in range(-2, 3):
            dead_points.append((20 + dr, 20 + dc))

    for t in range(n_frames):
        # 大範圍、低峰值的高斯場（模擬配重塊壓力分佈）
        # peak ≈ 80g，σ=10，範圍涵蓋大部分 40×40
        peak = 80 + np.random.normal(0, 1.5)
        gt = gaussian_blob(20, 20, peak, 10)
        # 全面受壓下串擾不明顯
        obs = add_sensor_artifacts(gt, dead_points=dead_points,
                                   frame_idx=t, is_transient=False)
        gt_series.append(gt)
        obs_series.append(obs)
    return np.array(gt_series), np.array(obs_series)


# ============================================================
# Scenario 6：Die Bonder 4×4 頂針陣列 — 測多點受壓是否會被連通域過濾誤殺
# ============================================================
def scenario_diebonder_array(n_frames=60):
    """
    模擬 Die Bonder 頂針陣列：4×4 共 16 根小頂針，每根只接觸 2×2 像素。
    這是「連通域過濾」最嚴峻的測試——每個壓點都很小，
    如果演算法只保留最大連通域，其他 15 根小頂針會被誤殺。

    這個情境是驗證 TCFv2 的 island removal 不會誤殺正當的多點壓力。
    """
    gt_series = []
    obs_series = []
    # 4×4 頂針陣列，間距 8 格保證間隔乾淨，從 (6, 6) 開始
    # σ=1.2 讓每根頂針只佔約 3×3 像素，中間有明顯間隔
    pin_positions = [(6 + i * 9, 6 + j * 9) for i in range(4) for j in range(4)]
    dead_points = [(8, 8), (24, 17)]  # 兩個死點剛好落在某些頂針附近

    for t in range(n_frames):
        gt = np.zeros((ROWS, COLS))
        for (cy, cx) in pin_positions:
            # 每根頂針：小範圍高壓，σ=1.2，只涵蓋 3×3
            peak = 150 + np.random.normal(0, 3)
            gt += gaussian_blob(cy, cx, peak, 1.2)
        obs = add_sensor_artifacts(gt, dead_points=dead_points,
                                   frame_idx=t, is_transient=False)
        gt_series.append(gt)
        obs_series.append(obs)
    return np.array(gt_series), np.array(obs_series)


if __name__ == "__main__":
    for name, fn in [("static", scenario_static),
                     ("dynamic", scenario_dynamic),
                     ("diebonder", scenario_diebonder),
                     ("mixed", scenario_mixed),
                     ("fullplate", scenario_fullplate_static),
                     ("diebonder_array", scenario_diebonder_array)]:
        gt, obs = fn()
        print(f"{name}: {gt.shape}, gt_peak={gt.max():.1f}, obs_peak={obs.max():.1f}")
