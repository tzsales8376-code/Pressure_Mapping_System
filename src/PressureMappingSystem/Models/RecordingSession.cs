namespace PressureMappingSystem.Models;

/// <summary>
/// 錄製會話，儲存一段連續的壓力量測資料
/// </summary>
public class RecordingSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public double DurationMs => EndTime.HasValue
        ? (EndTime.Value - StartTime).TotalMilliseconds
        : (DateTime.Now - StartTime).TotalMilliseconds;

    /// <summary>錄製的所有幀</summary>
    public List<SensorFrame> Frames { get; set; } = new();

    /// <summary>錄製時使用的校正配方 ID</summary>
    public string CalibrationRecipeId { get; set; } = string.Empty;

    /// <summary>錄製設定的目標 FPS</summary>
    public int TargetFps { get; set; }

    /// <summary>實際平均 FPS</summary>
    public double ActualFps => Frames.Count > 1 && DurationMs > 0
        ? (Frames.Count - 1) / (DurationMs / 1000.0)
        : 0;

    /// <summary>觸發條件（若有）</summary>
    public TriggerCondition? Trigger { get; set; }

    /// <summary>是否正在錄製</summary>
    public bool IsRecording { get; set; }

    /// <summary>整段錄製中的全域峰值壓力幀</summary>
    public SensorFrame? GlobalPeakFrame { get; set; }

    /// <summary>
    /// 每個感測點在整段錄製過程中的最大壓力 (PEAK 顯示功能)
    /// </summary>
    public double[,] PeakPressureMap { get; set; } = new double[SensorConfig.Rows, SensorConfig.Cols];

    /// <summary>
    /// 新增一幀並更新 Peak 資料
    /// </summary>
    public void AddFrame(SensorFrame frame)
    {
        Frames.Add(frame);

        // 更新 Peak Pressure Map
        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                if (frame.PressureGrams[r, c] > PeakPressureMap[r, c])
                    PeakPressureMap[r, c] = frame.PressureGrams[r, c];
            }
        }

        // 更新全域峰值幀
        if (GlobalPeakFrame == null || frame.PeakPressureGrams > GlobalPeakFrame.PeakPressureGrams)
        {
            GlobalPeakFrame = frame.DeepClone();
        }
    }

    /// <summary>
    /// 重新計算 Peak Map（例如回放時使用）
    /// </summary>
    public void RecomputePeakMap()
    {
        PeakPressureMap = new double[SensorConfig.Rows, SensorConfig.Cols];
        GlobalPeakFrame = null;

        foreach (var frame in Frames)
        {
            for (int r = 0; r < SensorConfig.Rows; r++)
                for (int c = 0; c < SensorConfig.Cols; c++)
                    if (frame.PressureGrams[r, c] > PeakPressureMap[r, c])
                        PeakPressureMap[r, c] = frame.PressureGrams[r, c];

            if (GlobalPeakFrame == null || frame.PeakPressureGrams > GlobalPeakFrame.PeakPressureGrams)
                GlobalPeakFrame = frame.DeepClone();
        }
    }
}

/// <summary>
/// 錄製觸發條件
/// </summary>
public class TriggerCondition
{
    /// <summary>觸發類型</summary>
    public TriggerType Type { get; set; } = TriggerType.Manual;

    /// <summary>壓力閾值 (g)，用於自動觸發</summary>
    public double ThresholdGrams { get; set; } = 50.0;

    /// <summary>需超過閾值的點數</summary>
    public int MinActivePoints { get; set; } = 1;

    /// <summary>觸發前預錄幀數</summary>
    public int PreTriggerFrames { get; set; } = 30;

    /// <summary>觸發後持續錄製幀數 (0=手動停止)</summary>
    public int PostTriggerFrames { get; set; } = 0;

    /// <summary>定時錄製時長 (秒)</summary>
    public double TimedDurationSeconds { get; set; } = 10.0;
}

public enum TriggerType
{
    Manual,          // 手動 Record/Stop
    PressureThreshold, // 壓力超過閾值自動觸發
    Timed            // 定時錄製
}
