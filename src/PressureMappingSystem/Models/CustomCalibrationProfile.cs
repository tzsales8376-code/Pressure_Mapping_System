namespace PressureMappingSystem.Models;

/// <summary>
/// 自訂校正設定檔 — 可儲存/載入，供使用者選擇
/// </summary>
public class CustomCalibrationProfile
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<CalibrationDataPoint> Points { get; set; } = new();

    /// <summary>
    /// 計算全域校正乘數（加權後/加權前的平均比值）
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

    public override string ToString() => Name;
}

public class CalibrationDataPoint
{
    public string WeightLabel { get; set; } = "";
    public double WeightGrams { get; set; }
    public double PreValue { get; set; }
    public double PostValue { get; set; }
}
