using PressureMappingSystem.Comparison.Models;

namespace PressureMappingSystem.Comparison.Services;

/// <summary>
/// 兩張快照的四分項加權評分。
///
/// 業界依據：
///   1. 重量準度（Weight）：基於 Total Force 相對誤差，線性衰減
///      formula: WeightScore = 100 × max(0, 1 − |ErrPercent| / Tolerance)
///
///   2. 形狀相似度（Shape）：SSIM (Structural Similarity Index Measure)
///      Wang, Z. et al. (2004) "Image quality assessment: From error visibility to structural similarity"
///      IEEE TIP 業界視覺品管標準
///
///   3. 位置吻合（Position）：CoP (Center of Pressure) 偏移
///      formula: PositionScore = 100 × max(0, 1 − Shift / MaxAllowedShift)
///
///   4. 面積吻合（Area）：IoU (Intersection over Union)
///      formula: IoU = |A ∩ B| / |A ∪ B| (受壓點集合)
///      AreaScore = 100 × IoU
///
/// 加權後 RawScore = ΣWi × Si；FinalScore 進入合格區（≥95）飽和為 100。
/// </summary>
public static class SnapshotComparisonService
{
    private const int Rows = 40;
    private const int Cols = 40;
    private const double NoiseFloor = 5.0;  // 視為「有受壓」的最低值

    public static ComparisonResult Compare(
        CapturedSnapshot reference,  // A = 黃金樣品
        CapturedSnapshot dut,        // B = 待測
        ScoringConfig config)
    {
        var result = new ComparisonResult { Config = config };

        if (reference == null || dut == null)
        {
            result.Warnings.Add("Comparison requires both Reference (A) and DUT (B) snapshots");
            return result;
        }

        // ── 計算各種差值 ──
        double sumAbsDiff = 0;
        double maxAbs = 0;
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                double d = Math.Abs(reference.PressureGrams[r, c] - dut.PressureGrams[r, c]);
                result.AbsDiff[r, c] = d;
                sumAbsDiff += d;
                if (d > maxAbs) maxAbs = d;
            }
        }
        result.MAE = sumAbsDiff / (Rows * Cols);
        result.MaxAbsDiff = maxAbs;

        // ── 摘要差 ──
        result.PeakDelta = dut.Peak - reference.Peak;
        result.TotalForceDelta = dut.TotalForce - reference.TotalForce;
        result.ActivePointsDelta = dut.ActivePoints - reference.ActivePoints;
        result.ContactAreaDelta = dut.ContactArea - reference.ContactArea;

        double dx = reference.CoPX - dut.CoPX;
        double dy = reference.CoPY - dut.CoPY;
        // CoP 是 row/col index，pitch 0.5mm 換算成 mm
        result.CoPShiftMm = Math.Sqrt(dx * dx + dy * dy) * 0.5;

        // ── 1. WeightScore（Total Force 準度） ──
        result.WeightScore = ComputeWeightScore(reference, dut, config, result);

        // ── 2. ShapeScore（SSIM） ──
        result.Ssim = ComputeSsim(reference.PressureGrams, dut.PressureGrams);
        result.ShapeScore = Math.Clamp(result.Ssim * 100.0, 0, 100);

        // ── 3. PositionScore（CoP shift） ──
        result.PositionScore = ComputePositionScore(result.CoPShiftMm, config);

        // ── 4. AreaScore（IoU） ──
        result.IoU = ComputeIoU(reference.PressureGrams, dut.PressureGrams);
        result.AreaScore = Math.Clamp(result.IoU * 100.0, 0, 100);

        // ── 加權平均：RawScore ──
        // 為防範使用者在 JSON 裡權重和 ≠ 1，正規化一次
        double sum = config.WeightSum;
        if (sum < 1e-6)
        {
            result.Warnings.Add("ScoringConfig: weights sum to zero, using equal 0.25 each");
            result.RawScore = (result.WeightScore + result.ShapeScore + result.PositionScore + result.AreaScore) / 4.0;
        }
        else
        {
            double w = config.WeightFactor / sum;
            double sh = config.ShapeFactor / sum;
            double p = config.PositionFactor / sum;
            double a = config.AreaFactor / sum;
            result.RawScore = w * result.WeightScore
                            + sh * result.ShapeScore
                            + p * result.PositionScore
                            + a * result.AreaScore;
        }
        result.RawScore = Math.Clamp(result.RawScore, 0, 100);

        // ── 飽和區：≥ PassZoneThreshold 顯示 100 ──
        result.IsInPassZone = result.RawScore >= config.PassZoneThreshold;
        result.FinalScore = result.IsInPassZone ? 100.0 : result.RawScore;

        // ── 合格判定 ──
        result.IsPass = result.FinalScore >= config.PassThreshold;

        return result;
    }

    // ─────────────────────────────────────────────────────
    private static double ComputeWeightScore(
        CapturedSnapshot a, CapturedSnapshot b,
        ScoringConfig cfg, ComparisonResult result)
    {
        if (a.TotalForce < 1e-6)
        {
            result.Warnings.Add("Weight score: reference total force ~ 0, set to 0");
            return 0;
        }

        double errPct = Math.Abs(b.TotalForce - a.TotalForce) / a.TotalForce * 100.0;
        result.WeightErrorPercent = errPct;

        if (cfg.WeightTolerancePercent < 1e-6)
            return errPct < 0.01 ? 100 : 0;  // 極嚴格邊界

        double score = 100.0 * (1.0 - errPct / cfg.WeightTolerancePercent);
        return Math.Clamp(score, 0, 100);
    }

    private static double ComputePositionScore(double shiftMm, ScoringConfig cfg)
    {
        if (cfg.MaxCopShiftMm < 1e-6) return shiftMm < 0.01 ? 100 : 0;
        double score = 100.0 * (1.0 - shiftMm / cfg.MaxCopShiftMm);
        return Math.Clamp(score, 0, 100);
    }

    /// <summary>
    /// IoU on active region:
    ///   有受壓的點集合 A = {(r,c) | a[r,c] > NoiseFloor}
    ///   有受壓的點集合 B = {(r,c) | b[r,c] > NoiseFloor}
    ///   IoU = |A ∩ B| / |A ∪ B|
    /// </summary>
    private static double ComputeIoU(double[,] a, double[,] b)
    {
        int inter = 0, union = 0;
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                bool inA = a[r, c] > NoiseFloor;
                bool inB = b[r, c] > NoiseFloor;
                if (inA && inB) inter++;
                if (inA || inB) union++;
            }
        if (union == 0) return 1.0;  // 兩邊都空 → 視為完全一致
        return (double)inter / union;
    }

    /// <summary>
    /// SSIM (Structural Similarity Index Measure)
    /// Wang et al. 2004
    ///
    /// 全域單窗版本（簡化版）：
    ///   μx, μy: 平均
    ///   σx², σy²: 變異數
    ///   σxy: 共變異數
    ///   c1 = (k1 × L)², c2 = (k2 × L)²
    ///   L = 動態範圍（取兩圖最大值）
    ///   k1 = 0.01, k2 = 0.03（業界標準常數）
    ///
    ///   SSIM = ((2μxμy + c1)(2σxy + c2)) / ((μx² + μy² + c1)(σx² + σy² + c2))
    ///
    /// 全域版對「整圖結構」的判讀夠用；若客戶要求多窗 mean SSIM 可後續擴充。
    /// </summary>
    private static double ComputeSsim(double[,] a, double[,] b)
    {
        int n = Rows * Cols;

        double sumA = 0, sumB = 0;
        double maxA = 0, maxB = 0;
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                sumA += a[r, c]; sumB += b[r, c];
                if (a[r, c] > maxA) maxA = a[r, c];
                if (b[r, c] > maxB) maxB = b[r, c];
            }
        double meanA = sumA / n;
        double meanB = sumB / n;

        double varA = 0, varB = 0, cov = 0;
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                double da = a[r, c] - meanA;
                double db = b[r, c] - meanB;
                varA += da * da;
                varB += db * db;
                cov += da * db;
            }
        varA /= (n - 1);
        varB /= (n - 1);
        cov /= (n - 1);

        // 動態範圍 L：取兩圖中較大的 max；避免兩圖都 0 造成 0/0
        double L = Math.Max(maxA, maxB);
        if (L < 1e-6) return 1.0;  // 兩圖都空 → 完全相似

        const double k1 = 0.01;
        const double k2 = 0.03;
        double c1 = (k1 * L) * (k1 * L);
        double c2 = (k2 * L) * (k2 * L);

        double num = (2 * meanA * meanB + c1) * (2 * cov + c2);
        double den = (meanA * meanA + meanB * meanB + c1) * (varA + varB + c2);
        if (den < 1e-12) return 0;
        double ssim = num / den;

        // SSIM 數學上 [-1, 1]，clamp 到 [0, 1] 給分
        return Math.Clamp(ssim, 0.0, 1.0);
    }
}
