using PressureMappingSystem.Models;

namespace PressureMappingSystem.Services;

/// <summary>
/// 校正曲線擬合服務。
///
/// 給定一組 (rawSum, weight) 樣本，擬合成 W = f(raw) 的曲線並回報擬合品質。
/// 兩種方法：
///   Linear:    W = slope × raw + intercept   （對小範圍、近似線性的資料最穩）
///   PowerLaw:  W = scale × raw^exp          （對薄膜電阻感測器物理最貼合；
///                                             R 隨 F 呈冪次，raw 也隨之冪次變化）
///
/// 兩者都用最小平方法封閉解，零迭代、零外部依賴，n≥2 即可解。
/// 回傳 FitResult 同時包含參數、R²、逐點殘差百分比。
/// </summary>
public static class CalibrationFitter
{
    public class FitResult
    {
        public CalibrationFitMethod Method { get; set; }
        public double[] Parameters { get; set; } = Array.Empty<double>();
        public double RSquared { get; set; }
        /// <summary>逐點殘差百分比 — 順序對應輸入 points，(fitted - target) / target × 100%</summary>
        public double[] Residuals { get; set; } = Array.Empty<double>();
        public bool IsValid { get; set; }
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// 擬合一組 (x=rawSum, y=weight) 資料點。
    /// </summary>
    public static FitResult Fit(
        IList<(double rawSum, double weightGrams)> points,
        CalibrationFitMethod method)
    {
        var result = new FitResult { Method = method };

        if (points == null || points.Count < 2)
        {
            result.Message = "需要至少 2 個校正點";
            return result;
        }

        switch (method)
        {
            case CalibrationFitMethod.Linear:
                return FitLinear(points);
            case CalibrationFitMethod.PowerLaw:
                return FitPowerLaw(points);
            default:
                result.Message = $"未知的擬合方法: {method}";
                return result;
        }
    }

    /// <summary>
    /// 線性擬合：y = a·x + b，最小平方法封閉解。
    /// </summary>
    private static FitResult FitLinear(IList<(double x, double y)> points)
    {
        int n = points.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        foreach (var (x, y) in points)
        {
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumX2 += x * x;
        }

        double denom = n * sumX2 - sumX * sumX;
        if (Math.Abs(denom) < 1e-12)
        {
            return new FitResult
            {
                Method = CalibrationFitMethod.Linear,
                Message = "資料點 x 值過於相近，無法擬合",
            };
        }

        double slope = (n * sumXY - sumX * sumY) / denom;
        double intercept = (sumY - slope * sumX) / n;

        // 計算 R²
        double meanY = sumY / n;
        double ssTot = 0, ssRes = 0;
        var residuals = new double[n];
        for (int i = 0; i < n; i++)
        {
            var (x, y) = points[i];
            double yPred = slope * x + intercept;
            ssTot += (y - meanY) * (y - meanY);
            ssRes += (y - yPred) * (y - yPred);
            residuals[i] = y > 0 ? (yPred - y) / y * 100.0 : 0;
        }
        double r2 = ssTot > 1e-12 ? 1.0 - ssRes / ssTot : 1.0;

        return new FitResult
        {
            Method = CalibrationFitMethod.Linear,
            Parameters = new[] { slope, intercept },
            RSquared = r2,
            Residuals = residuals,
            IsValid = true,
            Message = $"Linear: W = {slope:G4}·raw + {intercept:G4}  (R²={r2:F4})",
        };
    }

    /// <summary>
    /// 冪次擬合：y = a·x^b  →  log(y) = log(a) + b·log(x)  —— 對 log 空間做線性回歸。
    /// 所有 (x, y) 必須 > 0；否則該點被忽略。
    /// </summary>
    private static FitResult FitPowerLaw(IList<(double x, double y)> points)
    {
        var logPts = new List<(double lx, double ly)>(points.Count);
        foreach (var (x, y) in points)
        {
            if (x > 0 && y > 0)
                logPts.Add((Math.Log(x), Math.Log(y)));
        }

        if (logPts.Count < 2)
        {
            return new FitResult
            {
                Method = CalibrationFitMethod.PowerLaw,
                Message = "PowerLaw 擬合需要至少 2 個 x > 0 且 y > 0 的資料點",
            };
        }

        // 對 (lx, ly) 做線性回歸
        int n = logPts.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        foreach (var (lx, ly) in logPts)
        {
            sumX += lx;
            sumY += ly;
            sumXY += lx * ly;
            sumX2 += lx * lx;
        }

        double denom = n * sumX2 - sumX * sumX;
        if (Math.Abs(denom) < 1e-12)
        {
            return new FitResult
            {
                Method = CalibrationFitMethod.PowerLaw,
                Message = "資料點 log(x) 值過於相近，無法擬合",
            };
        }

        double exponent = (n * sumXY - sumX * sumY) / denom;
        double logScale = (sumY - exponent * sumX) / n;
        double scale = Math.Exp(logScale);

        // 計算 R² — 在原始空間計算（以 W 為 y）
        double meanY = 0;
        int countOrig = 0;
        foreach (var (_, y) in points)
        {
            if (y > 0) { meanY += y; countOrig++; }
        }
        if (countOrig == 0)
        {
            return new FitResult { Method = CalibrationFitMethod.PowerLaw, Message = "無有效 y 值" };
        }
        meanY /= countOrig;

        double ssTot = 0, ssRes = 0;
        var residuals = new double[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            var (x, y) = points[i];
            if (x <= 0 || y <= 0) { residuals[i] = double.NaN; continue; }
            double yPred = scale * Math.Pow(x, exponent);
            ssTot += (y - meanY) * (y - meanY);
            ssRes += (y - yPred) * (y - yPred);
            residuals[i] = (yPred - y) / y * 100.0;
        }
        double r2 = ssTot > 1e-12 ? 1.0 - ssRes / ssTot : 1.0;

        return new FitResult
        {
            Method = CalibrationFitMethod.PowerLaw,
            Parameters = new[] { scale, exponent },
            RSquared = r2,
            Residuals = residuals,
            IsValid = true,
            Message = $"PowerLaw: W = {scale:G4}·raw^{exponent:F4}  (R²={r2:F4})",
        };
    }
}
