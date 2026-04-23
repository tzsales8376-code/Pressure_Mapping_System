using PressureMappingSystem.Models;

namespace PressureMappingSystem.Services;

/// <summary>
/// 空間訊號濾波器介面：處理一幀 40×40 壓力資料。
///
/// 實作需求：
/// - Process 會直接修改傳入的陣列（in-place）
/// - 濾波器可能有內部狀態（如 TCF 的 ring buffer），切換來源時呼叫 Reset 清除
/// </summary>
public interface ISignalFilter
{
    string Name { get; }
    void Reset();
    void Process(double[,] pressureGrams);
}

/// <summary>濾波器模式</summary>
public enum FilterMode
{
    /// <summary>原本的 erosion + inpainting + gaussian 管線</summary>
    Legacy = 0,
    /// <summary>時間一致性濾波（TCF）</summary>
    TemporalCoherence = 1,
}

/// <summary>
/// 濾波器工廠：依 FilterMode 建立對應的濾波器實例。
/// </summary>
public static class SignalFilterFactory
{
    public static ISignalFilter Create(FilterMode mode, FilterParameters prms)
    {
        return mode switch
        {
            FilterMode.TemporalCoherence => new TemporalCoherenceFilter(prms),
            _ => new LegacySignalFilter(prms),
        };
    }
}

/// <summary>
/// 濾波器共用參數（由 ViewModel 傳入）。
/// Legacy 只用到 NoiseFloorGrams / NoiseFilterPercent，
/// TCF 額外用到 ResponseMs / StabilityThreshold。
/// </summary>
public class FilterParameters
{
    public double NoiseFloorGrams { get; set; } = 5.0;
    public double NoiseFilterPercent { get; set; } = 0;
    public double Fps { get; set; } = 60;
    public double ResponseMs { get; set; } = 80;         // TCF 時間窗
    public double StabilityThreshold { get; set; } = 15; // TCF 穩定度判斷門檻
}
