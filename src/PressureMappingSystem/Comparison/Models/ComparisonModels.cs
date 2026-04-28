namespace PressureMappingSystem.Comparison.Models;

/// <summary>
/// 比對模式 — 決定怎麼解讀和呈現兩張快照的差異。
/// 三種模式底層演算法相同（差值、相關係數、誤差），差別在重點與報告呈現。
/// </summary>
public enum ComparisonMode
{
    /// <summary>樣品-樣品（重複性比對）：強調「差有多大」、不判合格</summary>
    SampleToSample = 0,

    /// <summary>基準-待測（OK/NG 判定）：以容差判定合格與否</summary>
    ReferenceVsDut = 1,

    /// <summary>前-後（變化方向）：紅藍雙色呈現增加/減少區域</summary>
    BeforeAfter = 2,
}

/// <summary>
/// 比對指標 — 兩張快照之間的所有計算結果。
/// 不同模式會用到不同欄位、視覺呈現也不同，但底層數據都在這裡。
/// </summary>
public class ComparisonResult
{
    /// <summary>使用的模式</summary>
    public ComparisonMode Mode { get; set; }

    /// <summary>40×40 有號差值 D[r,c] = A[r,c] - B[r,c]（用於 Before-After 雙色圖）</summary>
    public double[,] SignedDiff { get; set; } = new double[40, 40];

    /// <summary>40×40 絕對差值 |D[r,c]|（用於熱力圖呈現）</summary>
    public double[,] AbsDiff { get; set; } = new double[40, 40];

    // ── 整體指標 ──
    /// <summary>Mean Absolute Error（g）</summary>
    public double MAE { get; set; }
    /// <summary>Root Mean Square Error（g）</summary>
    public double RMSE { get; set; }
    /// <summary>Pearson 相關係數（-1 ~ 1，越接近 1 兩張圖形狀越像）</summary>
    public double PearsonR { get; set; }
    /// <summary>絕對差值的最大值（g）</summary>
    public double MaxAbsDiff { get; set; }

    // ── 整幅摘要差 ──
    public double PeakDelta { get; set; }
    public double TotalForceDelta { get; set; }
    public int ActivePointsDelta { get; set; }
    public double ContactAreaDelta { get; set; }
    /// <summary>CoP 偏移量（mm，歐氏距離）</summary>
    public double CoPShiftMm { get; set; }

    // ── Reference vs DUT 模式專用 ──
    /// <summary>使用的容差設定（g）</summary>
    public double ToleranceGrams { get; set; }
    /// <summary>使用的容差設定（%）</summary>
    public double TolerancePercent { get; set; }
    /// <summary>是否合格（OK/NG）</summary>
    public bool IsPass { get; set; }
    /// <summary>超出容差的點數</summary>
    public int OutOfToleranceCount { get; set; }
    /// <summary>超出容差的點佔比（%）</summary>
    public double OutOfTolerancePercent { get; set; }

    // ── Before-After 模式專用 ──
    /// <summary>正向變化（A > B）的總和</summary>
    public double TotalIncrease { get; set; }
    /// <summary>負向變化（A < B）的總和</summary>
    public double TotalDecrease { get; set; }
    /// <summary>淨變化 = TotalIncrease - TotalDecrease</summary>
    public double NetChange => TotalIncrease - TotalDecrease;

    /// <summary>計算過程的警告（資料異常時）</summary>
    public List<string> Warnings { get; set; } = new();
}
