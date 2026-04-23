namespace PressureMappingSystem.Models;

/// <summary>
/// 單幀感測器資料，包含 1600 個點的電阻值與轉換後的壓力值
/// </summary>
public class SensorFrame
{
    /// <summary>幀序號</summary>
    public long FrameIndex { get; set; }

    /// <summary>時間戳 (ms)</summary>
    public double TimestampMs { get; set; }

    /// <summary>原始電阻值 (KΩ)，40x40 陣列，[row, col]</summary>
    public double[,] RawResistance { get; set; }

    /// <summary>校正後壓力值 (g)，40x40 陣列，[row, col]</summary>
    public double[,] PressureGrams { get; set; }

    /// <summary>此幀的總受力面積 (mm²)，即有效觸發點數 × 單點面積</summary>
    public double ContactAreaMm2 { get; set; }

    /// <summary>此幀的總壓力 (g)</summary>
    public double TotalForceGrams { get; set; }

    /// <summary>此幀的最大單點壓力 (g)</summary>
    public double PeakPressureGrams { get; set; }

    /// <summary>最大壓力點的行座標</summary>
    public int PeakRow { get; set; }

    /// <summary>最大壓力點的列座標</summary>
    public int PeakCol { get; set; }

    /// <summary>有效觸發點數量</summary>
    public int ActivePointCount { get; set; }

    /// <summary>平均壓力 (g)，僅計算有效觸發點</summary>
    public double AveragePressureGrams { get; set; }

    /// <summary>壓力重心 X (mm)</summary>
    public double CenterOfPressureX { get; set; }

    /// <summary>壓力重心 Y (mm)</summary>
    public double CenterOfPressureY { get; set; }

    /// <summary>是否已由二進位解析器預先填入 PressureGrams（跳過 R→F 校正）</summary>
    public bool IsPreCalibrated { get; set; }

    public SensorFrame()
    {
        RawResistance = new double[SensorConfig.Rows, SensorConfig.Cols];
        PressureGrams = new double[SensorConfig.Rows, SensorConfig.Cols];
    }

    /// <summary>
    /// 深拷貝此幀
    /// </summary>
    public SensorFrame DeepClone()
    {
        var clone = new SensorFrame
        {
            FrameIndex = FrameIndex,
            TimestampMs = TimestampMs,
            IsPreCalibrated = IsPreCalibrated,
            ContactAreaMm2 = ContactAreaMm2,
            TotalForceGrams = TotalForceGrams,
            PeakPressureGrams = PeakPressureGrams,
            PeakRow = PeakRow,
            PeakCol = PeakCol,
            ActivePointCount = ActivePointCount,
            AveragePressureGrams = AveragePressureGrams,
            CenterOfPressureX = CenterOfPressureX,
            CenterOfPressureY = CenterOfPressureY
        };
        Array.Copy(RawResistance, clone.RawResistance, RawResistance.Length);
        Array.Copy(PressureGrams, clone.PressureGrams, PressureGrams.Length);
        return clone;
    }

    /// <summary>
    /// 根據電阻值計算統計資訊（在校正轉換後呼叫）
    /// </summary>
    public void ComputeStatistics()
    {
        double totalForce = 0;
        double peakPressure = 0;
        int peakR = 0, peakC = 0;
        int activeCount = 0;
        double weightedX = 0, weightedY = 0;

        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                double p = PressureGrams[r, c];
                if (p >= SensorConfig.TriggerForceGrams)
                {
                    activeCount++;
                    totalForce += p;
                    weightedX += c * SensorConfig.SensorPitchMm * p;
                    weightedY += r * SensorConfig.SensorPitchMm * p;

                    if (p > peakPressure)
                    {
                        peakPressure = p;
                        peakR = r;
                        peakC = c;
                    }
                }
            }
        }

        ActivePointCount = activeCount;
        TotalForceGrams = totalForce;
        PeakPressureGrams = peakPressure;
        PeakRow = peakR;
        PeakCol = peakC;
        ContactAreaMm2 = activeCount * SensorConfig.SensingAreaMm * SensorConfig.SensingAreaMm;
        AveragePressureGrams = activeCount > 0 ? totalForce / activeCount : 0;

        if (totalForce > 0)
        {
            CenterOfPressureX = weightedX / totalForce;
            CenterOfPressureY = weightedY / totalForce;
        }
    }
}
