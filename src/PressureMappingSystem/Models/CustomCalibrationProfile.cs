namespace PressureMappingSystem.Models;

/// <summary>
/// 自訂校正設定檔 — 可儲存/載入，供使用者選擇
///
/// v2 改動（向後相容）：新增 FitMethod、FitParameters、RSquared、Operator 欄位。
/// 舊版沒有這些欄位的 JSON 讀進來後 FitMethod 會是 Linear（預設），
/// FitParameters 會是空陣列，ApplyCurve 此時回傳原 raw，呼叫端會退回舊邏輯（ComputeGlobalFactor）。
/// </summary>
public class CustomCalibrationProfile
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<CalibrationDataPoint> Points { get; set; } = new();

    /// <summary>擬合方法。舊檔沒此欄位時會落到 Linear。</summary>
    public CalibrationFitMethod FitMethod { get; set; } = CalibrationFitMethod.Linear;

    /// <summary>
    /// 擬合後的參數。具體意義依 FitMethod 不同：
    ///   Linear:   Parameters[0]=slope (weight per raw unit)
    ///             Parameters[1]=intercept
    ///   PowerLaw: Parameters[0]=scale
    ///             Parameters[1]=exponent (W = scale × raw^exponent)
    /// </summary>
    public double[] FitParameters { get; set; } = Array.Empty<double>();

    /// <summary>擬合的決定係數 (R²)；0~1，越接近 1 越好</summary>
    public double RSquared { get; set; }

    /// <summary>操作員（選填）</summary>
    public string Operator { get; set; } = "";

    /// <summary>
    /// 計算全域校正乘數（加權後/加權前的平均比值）—
    /// 舊程式的 fallback：當 FitParameters 為空時使用
    /// </summary>
    public double ComputeGlobalFactor()
    {
        double totalFactor = 0;
        int valid = 0;
        foreach (var pt in Points)
        {
            if (pt.PreValue > 0 && pt.PostValue > 0)
            {
                totalFactor += pt.PostValue / pt.PreValue;
                valid++;
            }
        }
        return valid > 0 ? totalFactor / valid : 1.0;
    }

    /// <summary>
    /// 把一個測量的 raw 讀數轉換為實際重量 (g)。
    /// 若未擬合，回傳 raw 本身。
    /// </summary>
    public double ApplyCurve(double rawReading)
    {
        if (FitParameters == null || FitParameters.Length < 2)
            return rawReading;

        switch (FitMethod)
        {
            case CalibrationFitMethod.Linear:
                return FitParameters[0] * rawReading + FitParameters[1];

            case CalibrationFitMethod.PowerLaw:
                if (rawReading <= 0) return 0;
                // W = scale × raw^exp
                return FitParameters[0] * Math.Pow(rawReading, FitParameters[1]);

            default:
                return rawReading;
        }
    }

    /// <summary>此 profile 是否有可用的擬合曲線</summary>
    public bool HasFit => FitParameters != null && FitParameters.Length >= 2;

    public override string ToString() => Name;
}

public class CalibrationDataPoint
{
    public string WeightLabel { get; set; } = "";
    public double WeightGrams { get; set; }

    /// <summary>
    /// 放上砝碼時感測器量到的讀數。
    /// v1 語意：所有活躍點的平均（avg pressure, 單位 g/cell）
    /// v2 語意：若同時有 RawSum 欄位，以 RawSum 為主；PreValue 維持原 v1 語意以相容舊檔
    /// </summary>
    public double PreValue { get; set; }

    /// <summary>校正後該讀數對應的實際重量（g）</summary>
    public double PostValue { get; set; }

    /// <summary>v2 新增：擷取當下的 raw sum（整片 1600 點的未校正壓力總和）</summary>
    public double RawSum { get; set; }

    /// <summary>v2 新增：擷取當下的 active cells（超過門檻的點數）</summary>
    public int ActiveCells { get; set; }

    /// <summary>v2 新增：擬合後的殘差百分比 ( (fitted - target) / target × 100% )</summary>
    public double ResidualPercent { get; set; }

    /// <summary>v2 新增：是否啟用此點參與擬合（UI 可勾選停用可疑點）</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>校正曲線擬合方法</summary>
public enum CalibrationFitMethod
{
    /// <summary>線性：W = slope × raw + intercept（最小平方法）</summary>
    Linear = 0,

    /// <summary>冪次：W = scale × raw^exponent（對數空間線性回歸）— 對薄膜電阻式感測器物理最貼合</summary>
    PowerLaw = 1,
}
