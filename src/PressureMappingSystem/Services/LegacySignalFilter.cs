using PressureMappingSystem.Models;

namespace PressureMappingSystem.Services;

/// <summary>
/// 既有的 spatial signal filter — 對應原 MainViewModel 的：
///   - 訊號純淨度兩階段濾波（Stage1 增強全域門檻 + Stage2 自適應邊緣侵蝕）
///   - SpatialSignalProcess (step 1 清孤立 + step 2 多輪填補 + step 2b 四方向強制填補 + step 3 Gaussian)
///
/// 這裡只是把原本散落在 MainViewModel 的邏輯搬過來；行為完全一致。
/// </summary>
public class LegacySignalFilter : ISignalFilter
{
    public string Name => "Legacy (空間濾波)";

    private readonly FilterParameters _p;

    public LegacySignalFilter(FilterParameters p)
    {
        _p = p;
    }

    public void Reset() { /* 無狀態 */ }

    public void Process(double[,] data)
    {
        int rows = SensorConfig.Rows;
        int cols = SensorConfig.Cols;

        // ────────────────────────────────────────
        // Stage 1+2：訊號純淨度兩階段濾波
        // ────────────────────────────────────────
        if (_p.NoiseFilterPercent > 0)
        {
            double framePeak = 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    if (data[r, c] > framePeak)
                        framePeak = data[r, c];

            // Stage 1：增強全域門檻
            if (framePeak > 0)
            {
                double dyn = framePeak * (_p.NoiseFilterPercent / 100.0) * 3.0;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                        if (data[r, c] > 0 && data[r, c] < dyn)
                            data[r, c] = 0;
            }

            // Stage 2：邊緣侵蝕（5×5 搜索）
            const double ErosionRatio = 0.35;
            int maxPasses = Math.Max(1, (int)(_p.NoiseFilterPercent * 1.5));
            for (int pass = 0; pass < maxPasses; pass++)
            {
                var snap = (double[,])data.Clone();
                bool anyEroded = false;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        if (snap[r, c] <= 0) continue;
                        bool isBoundary = false;
                        double maxNeighbor = 0;
                        for (int dr = -2; dr <= 2; dr++)
                            for (int dc = -2; dc <= 2; dc++)
                            {
                                if (dr == 0 && dc == 0) continue;
                                int nr = r + dr, nc = c + dc;
                                if (nr < 0 || nr >= rows || nc < 0 || nc >= cols)
                                {
                                    if (Math.Abs(dr) <= 1 && Math.Abs(dc) <= 1)
                                        isBoundary = true;
                                    continue;
                                }
                                if (snap[nr, nc] <= 0)
                                {
                                    if (Math.Abs(dr) <= 1 && Math.Abs(dc) <= 1)
                                        isBoundary = true;
                                }
                                else if (snap[nr, nc] > maxNeighbor)
                                {
                                    maxNeighbor = snap[nr, nc];
                                }
                            }
                        if (isBoundary && maxNeighbor > 0 && snap[r, c] < maxNeighbor * ErosionRatio)
                        {
                            data[r, c] = 0;
                            anyEroded = true;
                        }
                    }
                if (!anyEroded) break;
            }
        }

        // ────────────────────────────────────────
        // Step 1：清除孤立雜訊
        // ────────────────────────────────────────
        {
            var snap = (double[,])data.Clone();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (snap[r, c] <= 0) continue;
                    CountNeighbors(snap, r, c, rows, cols, out int ac, out _);
                    if (ac <= 1)
                    {
                        int wideCount = 0;
                        for (int dr = -2; dr <= 2; dr++)
                            for (int dc = -2; dc <= 2; dc++)
                            {
                                if (dr == 0 && dc == 0) continue;
                                int nr = r + dr, nc = c + dc;
                                if (nr >= 0 && nr < rows && nc >= 0 && nc < cols && snap[nr, nc] > 0)
                                    wideCount++;
                            }
                        if (wideCount <= 3) data[r, c] = 0;
                    }
                }
        }

        // ────────────────────────────────────────
        // Step 2：多輪迭代 Inpainting
        // ────────────────────────────────────────
        {
            const int MaxFillPasses = 10;
            for (int pass = 0; pass < MaxFillPasses; pass++)
            {
                var snap = (double[,])data.Clone();
                bool anyFilled = false;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        if (snap[r, c] > 0) continue;
                        CountNeighbors(snap, r, c, rows, cols, out int ac, out double asum);
                        if (ac >= 2)
                        {
                            data[r, c] = asum / ac;
                            anyFilled = true;
                        }
                    }
                if (!anyFilled) break;
            }
        }

        // ────────────────────────────────────────
        // Step 2b：內部空洞強制填補（四方向）
        // ────────────────────────────────────────
        {
            const int SearchRadius = 5;
            bool anyFilled = true;
            int maxRounds = 3;
            for (int round = 0; round < maxRounds && anyFilled; round++)
            {
                anyFilled = false;
                var snap = (double[,])data.Clone();
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
                            anyFilled = true;
                        }
                    }
            }
        }

        // ────────────────────────────────────────
        // Step 3：Gaussian 平滑 (σ=0.8, 3×3)
        // ────────────────────────────────────────
        {
            const double s2 = 0.8 * 0.8 * 2.0;
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
    }

    private static void CountNeighbors(double[,] data, int r, int c, int rows, int cols,
        out int activeCount, out double activeSum)
    {
        activeCount = 0;
        activeSum = 0;
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                int nr = r + dr, nc = c + dc;
                if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
                double v = data[nr, nc];
                if (v > 0) { activeCount++; activeSum += v; }
            }
    }
}
