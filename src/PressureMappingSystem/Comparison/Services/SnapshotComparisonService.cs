using PressureMappingSystem.Comparison.Models;

namespace PressureMappingSystem.Comparison.Services;

/// <summary>
/// 兩張 CapturedSnapshot 的比對核心演算法。
///
/// 設計思路：
///   底層數據（差值矩陣、MAE、相關係數等）三模式都計算，
///   只有 Reference vs DUT 模式額外計算「超容差判定」、
///   只有 Before-After 模式額外計算「正/負變化分量」。
///   呼叫端依 Mode 決定要顯示哪些欄位、用什麼配色。
/// </summary>
public static class SnapshotComparisonService
{
    /// <summary>
    /// 比對兩張快照
    /// </summary>
    /// <param name="a">A 快照</param>
    /// <param name="b">B 快照</param>
    /// <param name="mode">比對模式</param>
    /// <param name="toleranceGrams">Reference-DUT 模式的絕對容差（g），其他模式忽略</param>
    /// <param name="tolerancePercent">Reference-DUT 模式的相對容差（%），相對於 A 的對應點值</param>
    public static ComparisonResult Compare(
        CapturedSnapshot a, CapturedSnapshot b,
        ComparisonMode mode,
        double toleranceGrams = 5.0,
        double tolerancePercent = 5.0)
    {
        var result = new ComparisonResult
        {
            Mode = mode,
            ToleranceGrams = toleranceGrams,
            TolerancePercent = tolerancePercent,
        };

        if (a == null || b == null)
        {
            result.Warnings.Add("比對需要 A 與 B 兩張快照");
            return result;
        }

        const int rows = 40, cols = 40;

        // ── Step 1: 元素差、絕對差、計算統計量 ──
        double sumAbsDiff = 0;
        double sumSqDiff = 0;
        double maxAbs = 0;
        int totalPoints = rows * cols;

        // For Pearson correlation
        double sumA = 0, sumB = 0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                sumA += a.PressureGrams[r, c];
                sumB += b.PressureGrams[r, c];
            }
        double meanA = sumA / totalPoints;
        double meanB = sumB / totalPoints;

        double cov = 0, varA = 0, varB = 0;
        double totalIncrease = 0, totalDecrease = 0;
        int outOfTolCount = 0;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double va = a.PressureGrams[r, c];
                double vb = b.PressureGrams[r, c];
                double d = va - vb;
                double abs = Math.Abs(d);

                result.SignedDiff[r, c] = d;
                result.AbsDiff[r, c] = abs;

                sumAbsDiff += abs;
                sumSqDiff += d * d;
                if (abs > maxAbs) maxAbs = abs;

                // Pearson 累積
                double da = va - meanA;
                double db = vb - meanB;
                cov += da * db;
                varA += da * da;
                varB += db * db;

                // Before-After 變化方向累積
                if (d > 0) totalIncrease += d;
                else totalDecrease += -d;

                // Reference-DUT 容差判定（只算非零點，避免大量背景 0 灌水）
                if (mode == ComparisonMode.ReferenceVsDut && (va > 0 || vb > 0))
                {
                    double allowedAbs = toleranceGrams;
                    double allowedRel = Math.Abs(va) * tolerancePercent / 100.0;
                    double allowed = Math.Max(allowedAbs, allowedRel);
                    if (abs > allowed) outOfTolCount++;
                }
            }
        }

        result.MAE = sumAbsDiff / totalPoints;
        result.RMSE = Math.Sqrt(sumSqDiff / totalPoints);
        result.MaxAbsDiff = maxAbs;
        result.TotalIncrease = totalIncrease;
        result.TotalDecrease = totalDecrease;

        // Pearson 相關係數
        double denom = Math.Sqrt(varA * varB);
        if (denom > 1e-12)
            result.PearsonR = cov / denom;
        else
            result.PearsonR = 0;  // 至少一邊全平坦時無法定義

        // ── Step 2: 摘要差 ──
        result.PeakDelta = a.Peak - b.Peak;
        result.TotalForceDelta = a.TotalForce - b.TotalForce;
        result.ActivePointsDelta = a.ActivePoints - b.ActivePoints;
        result.ContactAreaDelta = a.ContactArea - b.ContactArea;

        double dx = a.CoPX - b.CoPX;
        double dy = a.CoPY - b.CoPY;
        // CoP 是 row/col index，乘以 pitch 0.5mm 換成 mm
        result.CoPShiftMm = Math.Sqrt(dx * dx + dy * dy) * 0.5;

        // ── Step 3: Reference-DUT 判定 ──
        if (mode == ComparisonMode.ReferenceVsDut)
        {
            // 統計有效點（非背景）作為分母
            int validPoints = 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    if (a.PressureGrams[r, c] > 0 || b.PressureGrams[r, c] > 0)
                        validPoints++;

            result.OutOfToleranceCount = outOfTolCount;
            result.OutOfTolerancePercent = validPoints > 0
                ? (double)outOfTolCount / validPoints * 100.0
                : 0;

            // 判定原則：
            //   1. 沒有任何點超出容差 → PASS
            //   2. 超出容差的點 < 5%（可能是邊緣雜訊）→ PASS（warning）
            //   3. 超出容差的點 ≥ 5% → FAIL
            //
            // 也可以用「Peak 必須在容差內」當硬性條件
            bool peakOk = Math.Abs(result.PeakDelta) <= Math.Max(toleranceGrams, a.Peak * tolerancePercent / 100.0);
            result.IsPass = peakOk && result.OutOfTolerancePercent < 5.0;

            if (!peakOk)
                result.Warnings.Add($"Peak 差 {result.PeakDelta:+0.0;-0.0;0} g 超出容差");
            if (result.OutOfTolerancePercent >= 5.0)
                result.Warnings.Add($"超容差點佔比 {result.OutOfTolerancePercent:F1}% ≥ 5%");
        }

        return result;
    }
}
