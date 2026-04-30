namespace PressureMappingSystem.Models;

/// <summary>
/// PMS-1600 感測器硬體規格配置
/// </summary>
public static class SensorConfig
{
    // ── 陣列尺寸 ──
    public const int Rows = 40;
    public const int Cols = 40;
    public const int TotalPoints = Rows * Cols; // 1600

    // ── 物理尺寸 (mm) ──
    public const double SensorPitchMm = 0.5;          // 感應點間距 0.5mm
    public const double SensingAreaMm = 0.3;           // 每點感應面積 0.3x0.3mm
    public const double GapMm = 0.2;                   // 感應點間隙
    public const double TotalWidthMm = Rows * SensorPitchMm;  // 20mm
    public const double TotalHeightMm = Cols * SensorPitchMm; // 20mm
    public const double ThicknessMm = 0.2;

    // ── 壓力範圍 (g) ──
    public const double MinForceGrams = 5.0;
    public const double MaxForceGrams = 1000.0;
    public const double TriggerForceGrams = 5.0;

    // ── 電阻範圍 (KΩ) ──
    public const double MinResistanceKOhm = 0.2;       // 最大壓力時的最小電阻
    public const double MaxResistanceKOhm = 500.0;     // 觸發壓力時的最大電阻
    public const double NoTouchResistanceKOhm = 1000.0; // 未觸發電阻 > 1MΩ
    public const double TriggerResistanceKOhm = 500.0;

    // ── 效能規格 ──
    public const double RepeatabilityPercent = 5.0;     // ±5% @1Kg
    public const double DriftPercent = 10.0;            // -10%, 24H@1Kg
    public const double HysteresisPercent = 10.0;       // 10%, 24H@1Kg
    public const int DurabilityCycles = 1_000_000;      // 100萬次 @1Kg
    public const double ResponseTimeMicroseconds = 10.0; // <10μs

    // ── 環境規格 ──
    public const double MinTemperatureCelsius = -20.0;
    public const double MaxTemperatureCelsius = 65.0;

    // ── ADC 設定 ──
    // ADC 滿量程參考值：原始 ADC = AdcFullScale 時對應 MaxForceGrams (1000g)
    // 12-bit ADC → 4096, 10-bit → 1024, 16-bit → 65536
    // 如實際感測器 ADC 不同，請調整此值
    public const int AdcFullScale = 4096;

    // ── 通訊設定 ──
    public const int DefaultBaudRate = 115200;
    public const int DefaultTargetFps = 60;

    // ── PIN 配置 ──
    public const int PinCount = 40;                     // X40 + Y40
    public const double PinPitchMm = 1.0;
}
