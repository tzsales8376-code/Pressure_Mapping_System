namespace PressureMappingSystem.Comparison.Models;

/// <summary>
/// 比對結果（基準-待測 / Reference vs DUT 模式專用）
///
/// 評分邏輯：四個分項各 0~100，加權平均後得 RawScore（0~100）。
/// 進入「合格區」（RawScore ≥ PassThreshold）時 FinalScore 飽和為 100，
/// 在分數備註區仍保留 raw 數字以供審計。
///
/// 業界依據：
///   - Weight (Total Force)：以 Tolerance 線性衰減
///   - Shape：SSIM（影像結構相似性，業界 QC 標準）
///   - Position：CoP 中心偏移、線性衰減
///   - Area：IoU（受壓區域 Intersection over Union）
/// </summary>
public class ComparisonResult
{
    /// <summary>40×40 絕對差值 |A − B|（顯示用）</summary>
    public double[,] AbsDiff { get; set; } = new double[40, 40];

    // ── 原始數值（A vs B 摘要差，供報告呈現） ──
    public double PeakDelta { get; set; }
    public double TotalForceDelta { get; set; }
    public int ActivePointsDelta { get; set; }
    public double ContactAreaDelta { get; set; }
    /// <summary>CoP 偏移量（mm，歐氏距離）</summary>
    public double CoPShiftMm { get; set; }
    /// <summary>絕對差值最大值（g）— Diff 圖色階用</summary>
    public double MaxAbsDiff { get; set; }
    /// <summary>整體 MAE（g）— 報告用</summary>
    public double MAE { get; set; }

    // ── 四個分項分數（0~100） ──
    /// <summary>重量準度分（基於 Total Force 與 Tolerance 線性衰減）</summary>
    public double WeightScore { get; set; }
    /// <summary>形狀相似度分（基於 SSIM）</summary>
    public double ShapeScore { get; set; }
    /// <summary>位置吻合分（基於 CoP shift 與 MaxCopShiftMm 線性衰減）</summary>
    public double PositionScore { get; set; }
    /// <summary>面積吻合分（基於 Active Points 的 IoU）</summary>
    public double AreaScore { get; set; }

    // ── 分項計算的中間值（報告與審計用） ──
    /// <summary>SSIM 原始值（0~1）</summary>
    public double Ssim { get; set; }
    /// <summary>IoU 原始值（0~1）</summary>
    public double IoU { get; set; }
    /// <summary>Total Force 相對誤差（%）</summary>
    public double WeightErrorPercent { get; set; }

    // ── 總分與判定 ──
    /// <summary>原始加權總分（0~100）</summary>
    public double RawScore { get; set; }
    /// <summary>顯示用最終分（≥ PassThreshold 時飽和為 100）</summary>
    public double FinalScore { get; set; }
    /// <summary>是否合格（FinalScore ≥ FailThreshold）</summary>
    public bool IsPass { get; set; }
    /// <summary>是否進入合格區（觸發飽和為 100）</summary>
    public bool IsInPassZone { get; set; }

    // ── 評分配置（記錄用） ──
    public ScoringConfig Config { get; set; } = new();

    /// <summary>實際採用的情境（自動模式判定後的結果，或使用者手動選擇的）</summary>
    public ScoringScenario AppliedScenario { get; set; } = ScoringScenario.SimilarObject;

    /// <summary>實際採用的 profile（依 AppliedScenario 從 Config 解出）</summary>
    public ScenarioProfile AppliedProfile { get; set; } = new();

    /// <summary>自動判定模式時的判定理由（顯示用，例如「IoU=0.78 ≥ 0.6 → 相似待測物」）</summary>
    public string AutoJudgmentReason { get; set; } = "";

    /// <summary>計算過程的警告（資料異常時）</summary>
    public List<string> Warnings { get; set; } = new();
}
