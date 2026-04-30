using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using PressureMappingSystem.Comparison.Models;
using PressureMappingSystem.Comparison.Services;
using PressureMappingSystem.Models;

namespace PressureMappingSystem.Comparison;

public partial class ComparisonWindow : Window
{
    private readonly CapturedSnapshot _a;     // Reference (黃金樣品)
    private readonly CapturedSnapshot _b;     // DUT (待測)
    private readonly string _exportFolder;
    private readonly ScoringConfig _config;
    private ComparisonResult? _result;
    private bool _initializing = true;

    public ComparisonWindow(CapturedSnapshot a, CapturedSnapshot b, string? exportFolder = null)
    {
        InitializeComponent();
        _a = a ?? throw new ArgumentNullException(nameof(a));
        _b = b ?? throw new ArgumentNullException(nameof(b));
        _exportFolder = exportFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TRANZX", "PressureData");

        // 載入評分配置（外部 JSON）
        _config = ScoringConfig.LoadOrCreateDefault();
        ConfigSourceText.Text = $"設定檔: {ScoringConfig.GetDefaultPath()}";

        // 預設選中項對應 JSON 內 Scenario
        ScenarioCombo.SelectedIndex = (int)_config.Scenario;

        // 統一色階：取兩者最大值
        double scaleMax = Math.Max(a.Peak, b.Peak);
        if (scaleMax < 1) scaleMax = SensorConfig.MaxForceGrams;

        HeatmapA.PressureData = a.PressureGrams;
        HeatmapA.ColorScaleMax = scaleMax;
        HeatmapA.ShowValues = false;
        HeatmapA.ShowGrid = true;
        HeatmapA.ShowCoP = true;
        HeatmapA.ShowLegend = false;
        InfoTextA.Text = FormatSnapshotInfo(a);

        HeatmapB.PressureData = b.PressureGrams;
        HeatmapB.ColorScaleMax = scaleMax;
        HeatmapB.ShowValues = false;
        HeatmapB.ShowGrid = true;
        HeatmapB.ShowCoP = true;
        HeatmapB.ShowLegend = false;
        InfoTextB.Text = FormatSnapshotInfo(b);

        _initializing = false;
        Recompute();
    }

    private void ScenarioCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        // 把使用者選的覆蓋進 _config 然後重算（不寫回 JSON、保持一次性）
        _config.Scenario = (ScoringScenario)ScenarioCombo.SelectedIndex;
        Recompute();
    }

    private static string FormatSnapshotInfo(CapturedSnapshot s)
    {
        return $"擷取時間: {s.CapturedAt:HH:mm:ss}\n" +
               $"Peak: {s.Peak:F1} g  Total: {s.TotalForce / 1000.0:F2} kg\n" +
               $"Active: {s.ActivePoints}/1600  Area: {s.ContactArea:F2} mm²\n" +
               $"CoP: ({s.CoPX * 0.5:F1}, {s.CoPY * 0.5:F1}) mm";
    }

    private void Recompute()
    {
        _result = SnapshotComparisonService.Compare(_a, _b, _config);
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (_result == null) return;

        // ── 絕對差圖 ──
        HeatmapAbsDiff.PressureData = _result.AbsDiff;
        HeatmapAbsDiff.ColorScaleMax = _result.MaxAbsDiff > 0 ? _result.MaxAbsDiff : 1.0;
        HeatmapAbsDiff.ShowGrid = true;
        HeatmapAbsDiff.ShowCoP = false;
        HeatmapAbsDiff.ShowValues = false;
        HeatmapAbsDiff.ShowLegend = false;
        DiffLegend.Text = $"色階：藍=低差異、紅=高差異（max {_result.MaxAbsDiff:F1} g）";

        // ── ScoreCard ──
        ScoreCard.Update(_result);

        // ── 摘要差 ──
        MetricPeakText.Text = $"ΔPeak    : {_result.PeakDelta:+0.0;-0.0;0} g";
        MetricTotalText.Text = $"ΔTotal   : {_result.TotalForceDelta:+0.0;-0.0;0} g ({_result.TotalForceDelta / 1000:+0.000;-0.000;0} kg)";
        MetricActiveText.Text = $"ΔActive  : {_result.ActivePointsDelta:+0;-0;0} pts";
        MetricCopText.Text = $"CoP shift: {_result.CoPShiftMm:F2} mm";
        MetricMaeText.Text = $"MAE      : {_result.MAE:F3} g";

        // ── Warnings ──
        if (_result.Warnings.Count > 0)
        {
            WarningPanel.Visibility = Visibility.Visible;
            WarningList.ItemsSource = _result.Warnings;
        }
        else
        {
            WarningPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void OpenConfigFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = ScoringConfig.GetDefaultPath();
        var folder = Path.GetDirectoryName(path);
        if (folder == null) return;
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = folder,
                UseShellExecute = true,
            });
        }
        catch
        {
            MessageBox.Show($"資料夾路徑：\n{folder}", "設定檔位置");
        }
    }

    private string BuildReportText()
    {
        if (_result == null) return string.Empty;
        var sb = new StringBuilder();
        sb.AppendLine("Pressure Snapshot Comparison Report");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("Mode: Reference vs DUT (基準-待測)");
        sb.AppendLine();

        sb.AppendLine("[Snapshot A — Reference]");
        sb.AppendLine($"  Captured: {_a.CapturedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  Peak={_a.Peak:F1}g  Total={_a.TotalForce / 1000:F2}kg  Active={_a.ActivePoints}");
        sb.AppendLine();

        sb.AppendLine("[Snapshot B — DUT]");
        sb.AppendLine($"  Captured: {_b.CapturedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  Peak={_b.Peak:F1}g  Total={_b.TotalForce / 1000:F2}kg  Active={_b.ActivePoints}");
        sb.AppendLine();

        sb.AppendLine("[Scoring Config]");
        sb.AppendLine($"  Scenario (selected) : {_result.Config.Scenario}");
        sb.AppendLine($"  Scenario (applied)  : {_result.AppliedScenario}");
        if (!string.IsNullOrEmpty(_result.AutoJudgmentReason))
            sb.AppendLine($"  Auto judgment       : {_result.AutoJudgmentReason}");
        var p = _result.AppliedProfile;
        sb.AppendLine($"  Weights: weight={p.WeightFactor:F2}, shape={p.ShapeFactor:F2}, " +
                      $"position={p.PositionFactor:F2}, area={p.AreaFactor:F2}");
        sb.AppendLine($"  Tolerances: weight ±{p.WeightTolerancePercent:F1}%, " +
                      $"max CoP shift {p.MaxCopShiftMm:F2}mm");
        sb.AppendLine($"  Thresholds: pass-zone {_result.Config.PassZoneThreshold:F0}, " +
                      $"pass {_result.Config.PassThreshold:F0}");
        sb.AppendLine();

        sb.AppendLine("[Score Breakdown]");
        sb.AppendLine($"  Weight   : {_result.WeightScore:F1} / 100  (Total error {_result.WeightErrorPercent:F2}%)");
        sb.AppendLine($"  Shape    : {_result.ShapeScore:F1} / 100  (SSIM={_result.Ssim:F4})");
        sb.AppendLine($"  Position : {_result.PositionScore:F1} / 100  (CoP shift {_result.CoPShiftMm:F2} mm)");
        sb.AppendLine($"  Area     : {_result.AreaScore:F1} / 100  (IoU={_result.IoU:F4})");
        sb.AppendLine();

        sb.AppendLine("[Final Result]");
        sb.AppendLine($"  Raw Score   : {_result.RawScore:F2} / 100");
        sb.AppendLine($"  Final Score : {_result.FinalScore:F0} / 100" +
                      (_result.IsInPassZone ? "  (saturated, in pass zone)" : ""));
        sb.AppendLine($"  Verdict     : {(_result.IsPass ? "PASS" : "FAIL")}");
        if (_result.VetoTriggered)
        {
            sb.AppendLine($"  Critical Veto: TRIGGERED");
            sb.AppendLine($"    Reason: {_result.VetoReason}");
        }
        sb.AppendLine();

        sb.AppendLine("[Summary Delta]");
        sb.AppendLine($"  ΔPeak  : {_result.PeakDelta:+0.0;-0.0;0} g");
        sb.AppendLine($"  ΔTotal : {_result.TotalForceDelta:+0.0;-0.0;0} g");
        sb.AppendLine($"  ΔActive: {_result.ActivePointsDelta:+0;-0;0} pts");
        sb.AppendLine($"  CoP shift: {_result.CoPShiftMm:F2} mm");
        sb.AppendLine($"  MAE    : {_result.MAE:F3} g");
        sb.AppendLine($"  Max|Δ| : {_result.MaxAbsDiff:F2} g");

        if (_result.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Warnings]");
            foreach (var w in _result.Warnings) sb.AppendLine($"  - {w}");
        }
        return sb.ToString();
    }

    private void SaveReport_Click(object sender, RoutedEventArgs e)
    {
        if (_result == null) return;
        try
        {
            Directory.CreateDirectory(_exportFolder);
            string filename = $"{DateTime.Now:yyyyMMdd_HHmmss}_Comparison_{(_result.IsPass ? "PASS" : "FAIL")}.txt";
            string fullPath = Path.Combine(_exportFolder, filename);
            File.WriteAllText(fullPath, BuildReportText(), Encoding.UTF8);
            MessageBox.Show($"比對報告已儲存至:\n{fullPath}", "匯出成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"儲存失敗：{ex.Message}", "錯誤",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
