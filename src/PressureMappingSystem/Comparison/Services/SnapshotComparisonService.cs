using PressureMappingSystem.Comparison.Models;

namespace PressureMappingSystem.Comparison.Services;

/// <summary>
/// 雙情境評分核心服務 v2。
///
/// 設計：
///   1. 依 ScoringConfig.Scenario 決定用哪套 ScenarioProfile
///      - Auto: 依 IoU 自動判斷
///      - SimilarObject: 受壓面積為主（重複量測場景）
///      - DifferentObject: 總重量為主（不同樣品同重場景）
///
///   2. 各分項計算「相似度」（0~1），再用業界非線性遞減曲線轉換為分數（0~100）
///      曲線分段：
///         85~100% → 95~100  (saturation, slope=1/3)
///         70~85%  → 80~95   (gentle decline, slope=1)
///         50~70%  → 50~80   (steep decline, slope=1.5)
///         30~50%  → 0~50    (steep decline, slope=2.5)
///         < 30%   → 0
///      這比純線性更貼近品管直覺：合格率 70% 拿 80 分，而非 70 分。
///
///   3. 加權合成 RawScore，≥ PassZoneThreshold 飽和為 100。
/// </summary>
public static class SnapshotComparisonService
{
    private const int Rows = 40;
    private const int Cols = 40;
    private const double NoiseFloor = 5.0;

    public static ComparisonResult Compare(
        CapturedSnapshot reference,
        CapturedSnapshot dut,
        ScoringConfig config)
    {
        var result = new ComparisonResult { Config = config };

        if (reference == null || dut == null)
        {
            result.Warnings.Add("Comparison requires both Reference (A) and DUT (B) snapshots");
            return result;
        }

        // ─── 1. 計算所有底層指標（與情境無關，全部都算）───
        ComputeBasicMetrics(reference, dut, result);

        // ─── 2. 決定 AppliedScenario ───
        ResolveScenario(config, result);

        // ─── 3. 取出對應 profile ───
        var profile = config.ResolveProfile(result.AppliedScenario);
        result.AppliedProfile = profile;

        // ─── 4. 各分項相似度 → 分數轉換 ───
        // Weight 分項：用 Tolerance 線性衰減（業界儀器校驗常用做法、不適合套非線性曲線）
        result.WeightScore = ComputeWeightScore(reference, dut, profile, result);

        // Shape / Area / Position：用「業界非線性遞減曲線」
        // SSIM ∈ [0, 1]，視為相似度
        result.ShapeScore = SimilarityToScore(result.Ssim);

        // IoU ∈ [0, 1]，視為相似度
        result.AreaScore = SimilarityToScore(result.IoU);

        // Position：CoP 偏移轉「相似度」（偏移 0 → 1.0，偏移 = MaxAllowed → 0.0）
        double positionSimilarity = profile.MaxCopShiftMm > 1e-6
            ? Math.Clamp(1.0 - result.CoPShiftMm / profile.MaxCopShiftMm, 0, 1)
            : (result.CoPShiftMm < 0.01 ? 1.0 : 0.0);
        result.PositionScore = SimilarityToScore(positionSimilarity);

        // ─── 5. 加權合成 ───
        ComputeWeightedTotal(result, profile);

        return result;
    }

    // ─────────────────────────────────────────────────────
    private static void ComputeBasicMetrics(CapturedSnapshot a, CapturedSnapshot b, ComparisonResult r)
    {
        // 差值矩陣
        double sumAbsDiff = 0, maxAbs = 0;
        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Cols; col++)
            {
                double d = Math.Abs(a.PressureGrams[row, col] - b.PressureGrams[row, col]);
                r.AbsDiff[row, col] = d;
                sumAbsDiff += d;
                if (d > maxAbs) maxAbs = d;
            }
        }
        r.MAE = sumAbsDiff / (Rows * Cols);
        r.MaxAbsDiff = maxAbs;

        // 摘要差
        r.PeakDelta = b.Peak - a.Peak;
        r.TotalForceDelta = b.TotalForce - a.TotalForce;
        r.ActivePointsDelta = b.ActivePoints - a.ActivePoints;
        r.ContactAreaDelta = b.ContactArea - a.ContactArea;

        double dx = a.CoPX - b.CoPX;
        double dy = a.CoPY - b.CoPY;
        r.CoPShiftMm = Math.Sqrt(dx * dx + dy * dy) * 0.5;

        // SSIM + IoU
        r.Ssim = ComputeSsim(a.PressureGrams, b.PressureGrams);
        r.IoU = ComputeIoU(a.PressureGrams, b.PressureGrams);

        // Weight error %
        if (a.TotalForce > 1e-6)
            r.WeightErrorPercent = Math.Abs(b.TotalForce - a.TotalForce) / a.TotalForce * 100.0;
        else
            r.WeightErrorPercent = 0;
    }

    private static void ResolveScenario(ScoringConfig config, ComparisonResult r)
    {
        if (config.Scenario != ScoringScenario.Auto)
        {
            r.AppliedScenario = config.Scenario;
            r.AutoJudgmentReason = "";
            return;
        }

        // 自動判斷：IoU ≥ AutoThresholdIou 為相似待測物
        if (r.IoU >= config.AutoThresholdIou)
        {
            r.AppliedScenario = ScoringScenario.SimilarObject;
            r.AutoJudgmentReason = $"IoU={r.IoU:F2} ≥ {config.AutoThresholdIou:F2} → 相似待測物";
        }
        else
        {
            r.AppliedScenario = ScoringScenario.DifferentObject;
            r.AutoJudgmentReason = $"IoU={r.IoU:F2} < {config.AutoThresholdIou:F2} → 不同待測物";
        }
    }

    private static double ComputeWeightScore(
        CapturedSnapshot a, CapturedSnapshot b,
        ScenarioProfile profile, ComparisonResult r)
    {
        if (a.TotalForce < 1e-6)
        {
            r.Warnings.Add("重量分數：基準總力 ~ 0、無法評分，給 0");
            return 0;
        }

        // 業界做法：「±X% 內 95 分以上」線性衰減
        // 你的需求：±5~10% 內 95+
        double tol = profile.WeightTolerancePercent;
        if (tol < 1e-6) return r.WeightErrorPercent < 0.01 ? 100 : 0;

        // 在 tol 內：100 ~ 95 線性（從完美到容差邊緣）
        // 超過 tol：95 ~ 0 線性（從容差邊緣到 2*tol）
        double err = r.WeightErrorPercent;
        if (err <= tol)
        {
            // 0 ~ tol → 100 ~ 95
            return 100.0 - 5.0 * (err / tol);
        }
        else if (err <= 2 * tol)
        {
            // tol ~ 2*tol → 95 ~ 0
            return 95.0 * (1.0 - (err - tol) / tol);
        }
        else
        {
            return 0;
        }
    }

    /// <summary>
    /// 業界非線性遞減曲線（分段線性）。
    /// 輸入：相似度 [0, 1]
    /// 輸出：分數 [0, 100]
    ///
    /// 對應點：
    ///   1.00 → 100
    ///   0.85 → 95   (進入飽和區)
    ///   0.70 → 80
    ///   0.50 → 50
    ///   0.30 → 0
    ///   < 0.30 → 0
    /// </summary>
    public static double SimilarityToScore(double similarity)
    {
        double s = Math.Clamp(similarity, 0, 1);

        if (s >= 0.85)
        {
            // 0.85~1.00 → 95~100  (slope ≈ 33)
            return 95.0 + (s - 0.85) / 0.15 * 5.0;
        }
        else if (s >= 0.70)
        {
            // 0.70~0.85 → 80~95   (slope = 100)
            return 80.0 + (s - 0.70) / 0.15 * 15.0;
        }
        else if (s >= 0.50)
        {
            // 0.50~0.70 → 50~80   (slope = 150)
            return 50.0 + (s - 0.50) / 0.20 * 30.0;
        }
        else if (s >= 0.30)
        {
            // 0.30~0.50 → 0~50    (slope = 250)
            return (s - 0.30) / 0.20 * 50.0;
        }
        else
        {
            return 0;
        }
    }

    private static void ComputeWeightedTotal(ComparisonResult r, ScenarioProfile profile)
    {
        double sum = profile.WeightSum;
        if (sum < 1e-6)
        {
            r.Warnings.Add("ScenarioProfile: 權重總和為 0，使用平均加權");
            r.RawScore = (r.WeightScore + r.ShapeScore + r.PositionScore + r.AreaScore) / 4.0;
        }
        else
        {
            // 正規化（防使用者填的權重和不為 1）
            double w = profile.WeightFactor / sum;
            double sh = profile.ShapeFactor / sum;
            double p = profile.PositionFactor / sum;
            double a = profile.AreaFactor / sum;
            r.RawScore = w * r.WeightScore
                       + sh * r.ShapeScore
                       + p * r.PositionScore
                       + a * r.AreaScore;
        }
        r.RawScore = Math.Clamp(r.RawScore, 0, 100);

        // 飽和區
        r.IsInPassZone = r.RawScore >= r.Config.PassZoneThreshold;
        r.FinalScore = r.IsInPassZone ? 100.0 : r.RawScore;

        // ── Critical Veto 一票否決機制 ──
        // 即使加權總分高，若關鍵指標嚴重失常，仍強制 FAIL
        // 這對應業界品管「重量超過 2× 容差視為嚴重異常」的實務做法
        CheckCriticalVeto(r, profile);

        // 合格判定（必須同時：加權總分達標 AND 沒觸發 veto）
        r.IsPass = r.FinalScore >= r.Config.PassThreshold && !r.VetoTriggered;
    }

    /// <summary>
    /// 檢查 critical veto 條件，若觸發則設置 VetoTriggered + VetoReason。
    ///
    /// 業界實務：當「重量誤差超過 N 倍容差」時，無論其他分項多好都應視為失敗。
    /// 例如：兩張壓力分布形狀完全相同（IoU=1, SSIM=1），但總重量差 30%，
    /// 在物理意義上不應被判 PASS（必有量測或樣品問題）。
    /// </summary>
    private static void CheckCriticalVeto(ComparisonResult r, ScenarioProfile profile)
    {
        // Weight veto：誤差超過 critical 倍率
        if (profile.IsWeightVetoEnabled && r.WeightErrorPercent > profile.WeightCriticalPercent)
        {
            r.VetoTriggered = true;
            r.VetoReason = $"重量誤差 {r.WeightErrorPercent:F2}% 超過 critical 容差 ±{profile.WeightCriticalPercent:F1}% " +
                          $"({profile.WeightCriticalMultiplier:F1}× 容差規格)，依品管實務一票否決";
        }

        // 未來若要加 Area / Position veto 在這裡擴充
    }

    // ─────────────────────────────────────────────────────
    // SSIM (Wang 2004) — 沿用 v1 實作
    private static double ComputeSsim(double[,] a, double[,] b)
    {
        int n = Rows * Cols;
        double sumA = 0, sumB = 0, maxA = 0, maxB = 0;
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

        double L = Math.Max(maxA, maxB);
        if (L < 1e-6) return 1.0;

        const double k1 = 0.01;
        const double k2 = 0.03;
        double c1 = (k1 * L) * (k1 * L);
        double c2 = (k2 * L) * (k2 * L);

        double num = (2 * meanA * meanB + c1) * (2 * cov + c2);
        double den = (meanA * meanA + meanB * meanB + c1) * (varA + varB + c2);
        if (den < 1e-12) return 0;
        return Math.Clamp(num / den, 0.0, 1.0);
    }

    // IoU — 沿用 v1
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
        if (union == 0) return 1.0;
        return (double)inter / union;
    }
}
