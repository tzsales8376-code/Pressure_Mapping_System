using PressureMappingSystem.Models;

namespace PressureMappingSystem.Services;

/// <summary>
/// Temporal-Coherence Filter (TCF)
///
/// 核心思想：為每個感測點維護最近 N 幀的時間統計量（μ、σ），用穩定性指標
///   s = μ / (σ + ε)
/// 來評估「這個點目前的讀數有多可信」，然後用 confidence 動態混合：
///   - 高 confidence（穩定壓力）→ 用 μ（時間平均，降噪）
///   - 低 confidence（雜訊或過渡）→ 用當前幀（靈敏度）
///
/// 靜態下：死點在 μ 上可能仍為 0 但鄰居穩定 → 由 inpainting 補
/// 動態下：邊緣串擾 μ 低、σ 低 → confidence 低 → 被 erosion 侵蝕
///
/// 演算法設計見模擬驗證：四個情境的 MAE 改善 87~90%、Ghost 降到 0%
/// （詳見 tcf_scenarios.py / tcf_compare_visual.py）
/// </summary>
public class TemporalCoherenceFilter : ISignalFilter
{
    public string Name => "TCF (時間一致性濾波)";

    private readonly FilterParameters _p;
    private readonly int _nHistory;           // ring buffer 長度
    private readonly double[][,] _history;    // 最近 N 幀
    private int _head;                        // 下一個要寫入的位置
    private int _count;                       // 已累積的幀數（≤ N）

    public TemporalCoherenceFilter(FilterParameters p)
    {
        _p = p;
        _nHistory = Math.Max(2, (int)(p.Fps * p.ResponseMs / 1000.0));
        _history = new double[_nHistory][,];
        for (int i = 0; i < _nHistory; i++)
            _history[i] = new double[SensorConfig.Rows, SensorConfig.Cols];
        _head = 0;
        _count = 0;
    }

    public void Reset()
    {
        _head = 0;
        _count = 0;
        // 清空歷史
        for (int i = 0; i < _nHistory; i++)
            for (int r = 0; r < SensorConfig.Rows; r++)
                for (int c = 0; c < SensorConfig.Cols; c++)
                    _history[i][r, c] = 0;
    }

    public void Process(double[,] data)
    {
        int rows = SensorConfig.Rows;
        int cols = SensorConfig.Cols;

        // ── Step 0：基本全域門檻 ──
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                if (data[r, c] < _p.NoiseFloorGrams)
                    data[r, c] = 0;

        // ── Step 1：寫入 history ring buffer ──
        var slot = _history[_head];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                slot[r, c] = data[r, c];

        _head = (_head + 1) % _nHistory;
        if (_count < _nHistory) _count++;

        // 若 buffer 還沒填滿，先跳過時間處理（避免 cold-start 不穩）
        // 仍需維持 in-place 介面，讓前幾幀直接輸出 raw
        if (_count < 2) return;

        // ── Step 2：計算每點的 μ, σ ──
        // 用 Welford's algorithm 避免數值不穩定
        var mu = new double[rows, cols];
        var variance = new double[rows, cols];
        for (int i = 0; i < _count; i++)
        {
            double weight = 1.0 / (i + 1);
            var hist = _history[i];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    double delta = hist[r, c] - mu[r, c];
                    mu[r, c] += delta * weight;
                    // M2 累積用於變異數
                    variance[r, c] += delta * (hist[r, c] - mu[r, c]);
                }
        }

        // ── Step 3：用 confidence 混合 μ 與當前幀 ──
        // 也順便計算 confidence 陣列供後續 step 使用
        var confidence = new double[rows, cols];
        const double eps = 1.0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                double sigma = _count > 1 ? Math.Sqrt(variance[r, c] / (_count - 1)) : 0;
                double stability = mu[r, c] / (sigma + eps);
                // sigmoid-like saturation: s / (s + threshold)
                double conf = stability / (stability + _p.StabilityThreshold);

                confidence[r, c] = conf;
                double current = data[r, c];
                data[r, c] = conf * mu[r, c] + (1.0 - conf) * current;
            }

        // ── Step 4：Confidence-weighted edge erosion ──
        // 只侵蝕「邊界 + 自己不穩定 + 鄰居也不強」的點
        // 避免把靜態的穩定邊緣也咬掉
        {
            const double ErosionRatio = 0.35;
            var snap = (double[,])data.Clone();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (snap[r, c] <= 0) continue;

                    bool hasZeroNeighbor = false;
                    double maxStableNeighbor = 0;

                    for (int dr = -2; dr <= 2; dr++)
                        for (int dc = -2; dc <= 2; dc++)
                        {
                            if (dr == 0 && dc == 0) continue;
                            int nr = r + dr, nc = c + dc;
                            if (nr < 0 || nr >= rows || nc < 0 || nc >= cols)
                            {
                                if (Math.Abs(dr) <= 1 && Math.Abs(dc) <= 1)
                                    hasZeroNeighbor = true;
                                continue;
                            }
                            if (snap[nr, nc] <= 0)
                            {
                                if (Math.Abs(dr) <= 1 && Math.Abs(dc) <= 1)
                                    hasZeroNeighbor = true;
                                continue;
                            }
                            // 鄰居的「可信壓力」 = 值 × 該點信心
                            double weighted = snap[nr, nc] * confidence[nr, nc];
                            if (weighted > maxStableNeighbor)
                                maxStableNeighbor = weighted;
                        }

                    // 邊界 + 自己信心低 + 鄰居也不夠強 → 侵蝕
                    if (hasZeroNeighbor && confidence[r, c] < 0.3)
                    {
                        if (maxStableNeighbor == 0 || snap[r, c] < maxStableNeighbor * ErosionRatio)
                            data[r, c] = 0;
                    }
                }
        }

        // ── Step 5：穩定鄰居空洞填補 ──
        // 只在 3 個以上穩定鄰居 (confidence > 0.5) 且 μ > noise floor 時才填
        // 確保填的是「確實在受壓區內的死點」而不是過渡殘影
        for (int pass = 0; pass < 5; pass++)
        {
            var snap = (double[,])data.Clone();
            bool anyFilled = false;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (snap[r, c] > 0) continue;
                    int stableCount = 0;
                    double nsum = 0;
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            if (dr == 0 && dc == 0) continue;
                            int nr = r + dr, nc = c + dc;
                            if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
                            if (snap[nr, nc] > 0 && confidence[nr, nc] > 0.5)
                            {
                                stableCount++;
                                nsum += snap[nr, nc];
                            }
                        }
                    if (stableCount >= 3 && mu[r, c] > _p.NoiseFloorGrams)
                    {
                        data[r, c] = nsum / stableCount;
                        anyFilled = true;
                    }
                }
            if (!anyFilled) break;
        }

        // ── Step 6：輕度 Gaussian 平滑（σ=0.6, 3×3）──
        // 比 Legacy 的 σ=0.8 略輕，因為時間平均已經降噪，不必再過度空間平滑
        {
            const double s2 = 0.6 * 0.6 * 2.0;
            var kernel = new double[3, 3];
            double ksum = 0;
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    double v = Math.Exp(-(dr * dr + dc * dc) / s2);
                    kernel[dr + 1, dc + 1] = v;
                    ksum += v;
                }
            for (int dr = 0; dr < 3; dr++)
                for (int dc = 0; dc < 3; dc++)
                    kernel[dr, dc] /= ksum;

            var snap = (double[,])data.Clone();
            for (int r = 1; r < rows - 1; r++)
                for (int c = 1; c < cols - 1; c++)
                {
                    if (snap[r, c] <= 0) continue;
                    double weighted = 0, wTotal = 0;
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            double val = snap[r + dr, c + dc];
                            double w = kernel[dr + 1, dc + 1];
                            if (val > 0 || (dr == 0 && dc == 0))
                            {
                                weighted += val * w;
                                wTotal += w;
                            }
                        }
                    if (wTotal > 0)
                        data[r, c] = weighted / wTotal;
                }
        }

        // ── 最終清理：平滑後低於門檻的殘餘歸零 ──
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                if (data[r, c] < _p.NoiseFloorGrams)
                    data[r, c] = 0;
    }
}
