using PressureMappingSystem.Models;

namespace PressureMappingSystem.Services;

/// <summary>
/// Temporal-Coherence Filter v2
///
/// 相較 v1 的四個改動：
///
/// 1. NoiseSuppression 滑桿取代 StabilityThreshold（語意翻轉）
///    v1: 滑桿小=嚴格、大=寬容（不直覺）
///    v2: 滑桿 0~100，大=更積極抑制雜訊；內部映射到 stability_threshold
///
/// 2. 梯度感知邊緣侵蝕（取代 v1 的 confidence < 0.3 判定）
///    對邊界點（有零鄰居）檢查：自己值 < peak×15%、最大鄰居 < peak×25%、
///    且 confidence < 0.4 才侵蝕。保留真實邊緣（有明顯壓力梯度）。
///
/// 3. 2-pass 空洞填補（取代 v1 的 3-穩定鄰居要求）
///    Pass 1: ≥5 個非零鄰居直接平均填
///    Pass 2: 四方向徑向外推（半徑 5）、3 個方向以上就填
///    目標：解決 v1 在 0.5kg 配重塊大空洞無法補齊的問題
///
/// 4. 連通域過濾（island removal）
///    8-連通分塊後，清除 importance < 主塊×island_ratio 的所有小島
///    目標：消滅遠離主區的孤立 ghost 點（客戶品管報告可接受度）
///
/// 另外：Cold-start 回退
///    ring buffer 未滿前（60 FPS × ResponseMs ≈ 12 幀）走簡化 Legacy 流程
///    避免開機前幾幀畫面怪異
///
/// Python 模擬驗證：6 個情境中 Fullplate 的 hole_ratio 從 TCFv1 的 1.0% 降至 0.0%、
/// PinArray ghost 從 Legacy 的 100% 降至 9.3%、其他情境相當或小幅改善。
/// </summary>
public class TemporalCoherenceFilter : ISignalFilter
{
    public string Name => "TCF v2 (時間一致性濾波)";

    private readonly FilterParameters _p;
    private readonly int _nHistory;           // ring buffer 長度
    private readonly double _stabilityThreshold;
    private readonly double[][,] _history;
    private int _head;
    private int _count;

    public TemporalCoherenceFilter(FilterParameters p)
    {
        _p = p;
        _nHistory = Math.Max(2, (int)(p.Fps * p.ResponseMs / 1000.0));
        // 將 0~100 映射到 stability_threshold（大 slider = 小 threshold = 更信任時間平均）
        _stabilityThreshold = Math.Max(3.0, 30.0 - 0.25 * p.NoiseSuppression);

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
        for (int i = 0; i < _nHistory; i++)
            for (int r = 0; r < SensorConfig.Rows; r++)
                for (int c = 0; c < SensorConfig.Cols; c++)
                    _history[i][r, c] = 0;
    }

    public void Process(double[,] data)
    {
        int rows = SensorConfig.Rows;
        int cols = SensorConfig.Cols;

        // Step 0: 基本門檻
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                if (data[r, c] < _p.NoiseFloorGrams)
                    data[r, c] = 0;

        // Step 1: 寫入 history ring buffer
        var slot = _history[_head];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                slot[r, c] = data[r, c];
        _head = (_head + 1) % _nHistory;
        if (_count < _nHistory) _count++;

        // Cold-start：未滿 ring buffer 用 Legacy 簡化流程
        if (_count < _nHistory)
        {
            ColdStartFallback(data);
            return;
        }

        // Step 2: 計算 μ, σ
        var mu = new double[rows, cols];
        var sigma2 = new double[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                double sum = 0;
                for (int i = 0; i < _count; i++) sum += _history[i][r, c];
                mu[r, c] = sum / _count;
                double ss = 0;
                for (int i = 0; i < _count; i++)
                {
                    double d = _history[i][r, c] - mu[r, c];
                    ss += d * d;
                }
                sigma2[r, c] = _count > 1 ? ss / (_count - 1) : 0;
            }

        // Step 3: confidence 混合
        var confidence = new double[rows, cols];
        const double eps = 1.0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                double sigma = Math.Sqrt(sigma2[r, c]);
                double stability = mu[r, c] / (sigma + eps);
                double conf = stability / (stability + _stabilityThreshold);
                confidence[r, c] = conf;
                data[r, c] = conf * mu[r, c] + (1 - conf) * data[r, c];
            }

        // Step 4: 梯度感知邊緣侵蝕
        GradientErosion(data, confidence);

        // Step 5: 2-pass inpainting
        InpaintPass1(data);
        InpaintPass2(data);

        // Step 6: 輕度 Gaussian 平滑
        GaussianSmooth(data);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                if (data[r, c] < _p.NoiseFloorGrams)
                    data[r, c] = 0;

        // Step 7: 連通域過濾
        RemoveSmallIslands(data);
    }

    // ─────────────────────────────────────────────────────
    private void ColdStartFallback(double[,] data)
    {
        // 簡化的 inpainting + 平滑，撐過 ring buffer 累積期
        int rows = SensorConfig.Rows;
        int cols = SensorConfig.Cols;

        for (int pass = 0; pass < 3; pass++)
        {
            var snap = (double[,])data.Clone();
            bool any = false;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (snap[r, c] > 0) continue;
                    double sum = 0;
                    int n = 0;
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            if (dr == 0 && dc == 0) continue;
                            int nr = r + dr, nc = c + dc;
                            if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
                            if (snap[nr, nc] > 0) { sum += snap[nr, nc]; n++; }
                        }
                    if (n >= 2)
                    {
                        data[r, c] = sum / n;
                        any = true;
                    }
                }
            if (!any) break;
        }
        GaussianSmooth(data);
    }

    // ─────────────────────────────────────────────────────
    private void GradientErosion(double[,] data, double[,] confidence)
    {
        int rows = SensorConfig.Rows;
        int cols = SensorConfig.Cols;
        double peak = 0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                if (data[r, c] > peak) peak = data[r, c];
        if (peak < 5) return;

        var snap = (double[,])data.Clone();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (snap[r, c] <= 0) continue;

                bool hasZero = false;
                double maxNb = 0;
                for (int dr = -1; dr <= 1; dr++)
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (dr == 0 && dc == 0) continue;
                        int nr = r + dr, nc = c + dc;
                        if (nr < 0 || nr >= rows || nc < 0 || nc >= cols)
                        {
                            hasZero = true;
                            continue;
                        }
                        if (snap[nr, nc] <= 0) hasZero = true;
                        else if (snap[nr, nc] > maxNb) maxNb = snap[nr, nc];
                    }

                if (!hasZero) continue;

                // Ghost 判定：自己值 < peak×15%、鄰居最大值 < peak×25%、confidence < 0.4
                if (snap[r, c] < peak * 0.15 && maxNb < peak * 0.25 && confidence[r, c] < 0.4)
                    data[r, c] = 0;
            }
    }

    // ─────────────────────────────────────────────────────
    private void InpaintPass1(double[,] data)
    {
        int rows = SensorConfig.Rows;
        int cols = SensorConfig.Cols;
        for (int pass = 0; pass < 5; pass++)
        {
            var snap = (double[,])data.Clone();
            bool any = false;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (snap[r, c] > 0) continue;
                    double sum = 0;
                    int n = 0;
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            if (dr == 0 && dc == 0) continue;
                            int nr = r + dr, nc = c + dc;
                            if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
                            if (snap[nr, nc] > 0) { sum += snap[nr, nc]; n++; }
                        }
                    if (n >= 5)
                    {
                        data[r, c] = sum / n;
                        any = true;
                    }
                }
            if (!any) break;
        }
    }

    private void InpaintPass2(double[,] data)
    {
        int rows = SensorConfig.Rows;
        int cols = SensorConfig.Cols;
        const int SearchRadius = 5;
        for (int pass = 0; pass < 3; pass++)
        {
            var snap = (double[,])data.Clone();
            bool any = false;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (snap[r, c] > 0) continue;
                    int dirs = 0;
                    double nsum = 0;
                    int ncount = 0;
                    for (int d = 1; d <= SearchRadius && r - d >= 0; d++)
                        if (snap[r - d, c] > 0) { dirs++; nsum += snap[r - d, c]; ncount++; break; }
                    for (int d = 1; d <= SearchRadius && r + d < rows; d++)
                        if (snap[r + d, c] > 0) { dirs++; nsum += snap[r + d, c]; ncount++; break; }
                    for (int d = 1; d <= SearchRadius && c - d >= 0; d++)
                        if (snap[r, c - d] > 0) { dirs++; nsum += snap[r, c - d]; ncount++; break; }
                    for (int d = 1; d <= SearchRadius && c + d < cols; d++)
                        if (snap[r, c + d] > 0) { dirs++; nsum += snap[r, c + d]; ncount++; break; }
                    if (dirs >= 3 && ncount > 0)
                    {
                        data[r, c] = nsum / ncount;
                        any = true;
                    }
                }
            if (!any) break;
        }
    }

    // ─────────────────────────────────────────────────────
    private void GaussianSmooth(double[,] data)
    {
        int rows = SensorConfig.Rows;
        int cols = SensorConfig.Cols;
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
                if (wTotal > 0) data[r, c] = weighted / wTotal;
            }
    }

    // ─────────────────────────────────────────────────────
    /// <summary>
    /// 連通域過濾：
    /// 1. 找所有 8-connected components
    /// 2. 每塊 importance = max × size
    /// 3. 清除 importance < 最大塊 × IslandThresholdRatio 的所有塊
    /// </summary>
    private void RemoveSmallIslands(double[,] data)
    {
        int rows = SensorConfig.Rows;
        int cols = SensorConfig.Cols;

        // 8-connected labeling（two-pass with union-find）
        var labels = new int[rows, cols];
        var parent = new List<int> { 0 };  // union-find parent array, 0 = background
        int nextLabel = 1;

        // Pass 1: assign labels
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (data[r, c] <= 0) continue;

                // 找已標記的鄰居（只看左、左上、上、右上）
                int minLabel = int.MaxValue;
                for (int dr = -1; dr <= 0; dr++)
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (dr == 0 && dc >= 0) continue;  // 只看左/上方向
                        int nr = r + dr, nc = c + dc;
                        if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
                        int l = labels[nr, nc];
                        if (l > 0 && l < minLabel) minLabel = l;
                    }

                if (minLabel == int.MaxValue)
                {
                    labels[r, c] = nextLabel;
                    parent.Add(nextLabel);  // parent[nextLabel] = nextLabel
                    nextLabel++;
                }
                else
                {
                    labels[r, c] = minLabel;
                    // Union with all connected neighbors
                    for (int dr = -1; dr <= 0; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            if (dr == 0 && dc >= 0) continue;
                            int nr = r + dr, nc = c + dc;
                            if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
                            int l = labels[nr, nc];
                            if (l > 0 && l != minLabel) Union(parent, minLabel, l);
                        }
                }
            }

        // Pass 2: 合併根標籤，計算 importance
        // importance[rootLabel] = (max_value, size)
        var rootMax = new Dictionary<int, double>();
        var rootSize = new Dictionary<int, int>();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (labels[r, c] == 0) continue;
                int root = Find(parent, labels[r, c]);
                labels[r, c] = root;
                double v = data[r, c];
                if (!rootMax.ContainsKey(root) || v > rootMax[root]) rootMax[root] = v;
                if (!rootSize.ContainsKey(root)) rootSize[root] = 0;
                rootSize[root]++;
            }

        if (rootMax.Count == 0) return;

        // 找最大 importance
        double maxImp = 0;
        foreach (var kvp in rootMax)
            if (rootSize.ContainsKey(kvp.Key))
            {
                double imp = kvp.Value * rootSize[kvp.Key];
                if (imp > maxImp) maxImp = imp;
            }
        if (maxImp <= 0) return;

        double threshold = maxImp * _p.IslandThresholdRatio;

        // 清除小於 threshold 的塊
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (labels[r, c] == 0) continue;
                int root = labels[r, c];
                if (!rootMax.ContainsKey(root) || !rootSize.ContainsKey(root)) continue;
                double imp = rootMax[root] * rootSize[root];
                if (imp < threshold) data[r, c] = 0;
            }
    }

    private static int Find(List<int> parent, int x)
    {
        while (parent[x] != x)
        {
            parent[x] = parent[parent[x]];  // path compression
            x = parent[x];
        }
        return x;
    }

    private static void Union(List<int> parent, int a, int b)
    {
        int ra = Find(parent, a);
        int rb = Find(parent, b);
        if (ra == rb) return;
        if (ra < rb) parent[rb] = ra;
        else parent[ra] = rb;
    }
}
