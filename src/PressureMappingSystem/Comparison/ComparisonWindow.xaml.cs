using System.Text;
using System.Windows;
using System.Windows.Controls;
using PressureMappingSystem.Comparison.Models;
using PressureMappingSystem.Comparison.Services;
using PressureMappingSystem.Models;

namespace PressureMappingSystem.Comparison;

public partial class ComparisonWindow : Window
{
    private readonly CapturedSnapshot _a;
    private readonly CapturedSnapshot _b;
    private readonly string _exportFolder;
    private ComparisonResult? _result;
    private bool _initializing = true;

    public ComparisonWindow(CapturedSnapshot a, CapturedSnapshot b, ComparisonMode initialMode = ComparisonMode.SampleToSample, string? exportFolder = null)
    {
        InitializeComponent();
        _a = a ?? throw new ArgumentNullException(nameof(a));
        _b = b ?? throw new ArgumentNullException(nameof(b));
        _exportFolder = exportFolder ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TRANZX", "PressureData");

        // 統一色階：取 A、B 兩者的最大值，確保視覺一致
        double scaleMax = Math.Max(a.Peak, b.Peak);
        if (scaleMax < 1) scaleMax = SensorConfig.MaxForceGrams;

        // 顯示 A、B 熱力圖（reuse 主程式的 HeatmapControl）
        HeatmapA.PressureData = a.PressureGrams;
        HeatmapA.ColorScaleMax = scaleMax;
        HeatmapA.ShowValues = false;
        HeatmapA.ShowGrid = true;
        HeatmapA.ShowCoP = true;
        HeatmapA.ShowLegend = false;   // 比對視窗不需要獨立色階，節省空間放大熱力圖
        InfoTextA.Text = FormatSnapshotInfo(a);

        HeatmapB.PressureData = b.PressureGrams;
        HeatmapB.ColorScaleMax = scaleMax;
        HeatmapB.ShowValues = false;
        HeatmapB.ShowGrid = true;
        HeatmapB.ShowCoP = true;
        HeatmapB.ShowLegend = false;
        InfoTextB.Text = FormatSnapshotInfo(b);

        ModeCombo.SelectedIndex = (int)initialMode;
        _initializing = false;

        Recompute();
    }

    private static string FormatSnapshotInfo(CapturedSnapshot s)
    {
        return $"擷取時間: {s.CapturedAt:HH:mm:ss}\n" +
               $"Peak: {s.Peak:F1} g  Total: {s.TotalForce / 1000.0:F2} kg\n" +
               $"Active: {s.ActivePoints}/1600  Area: {s.ContactArea:F2} mm²\n" +
               $"CoP: ({s.CoPX * 0.5:F1}, {s.CoPY * 0.5:F1}) mm";
    }

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        Recompute();
    }

    private void Tolerance_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        Recompute();
    }

    private void Recompute()
    {
        var mode = (ComparisonMode)ModeCombo.SelectedIndex;
        double tolG = 5.0, tolP = 5.0;
        if (mode == ComparisonMode.ReferenceVsDut)
        {
            double.TryParse(ToleranceGramsBox.Text, out tolG);
            double.TryParse(TolerancePercentBox.Text, out tolP);
            if (tolG <= 0) tolG = 5.0;
            if (tolP <= 0) tolP = 5.0;
        }

        _result = SnapshotComparisonService.Compare(_a, _b, mode, tolG, tolP);
        UpdateDisplay(mode);
    }

    private void UpdateDisplay(ComparisonMode mode)
    {
        if (_result == null) return;

        // ── Diff 圖切換：BeforeAfter 用紅藍雙色，其他用絕對差 jet ──
        if (mode == ComparisonMode.BeforeAfter)
        {
            HeatmapAbsDiff.Visibility = Visibility.Collapsed;
            HeatmapSignedDiff.Visibility = Visibility.Visible;
            HeatmapSignedDiff.SignedDiff = _result.SignedDiff;
            HeatmapSignedDiff.SymmetricMax = 0;  // 自動
            DiffTitle.Text = "差值（A − B，紅=A大、藍=A小）";
            DiffLegend.Text = $"色階範圍 ±{_result.MaxAbsDiff:F1} g";
        }
        else
        {
            HeatmapAbsDiff.Visibility = Visibility.Visible;
            HeatmapSignedDiff.Visibility = Visibility.Collapsed;
            HeatmapAbsDiff.PressureData = _result.AbsDiff;
            HeatmapAbsDiff.ColorScaleMax = _result.MaxAbsDiff > 0 ? _result.MaxAbsDiff : 1.0;
            HeatmapAbsDiff.ShowGrid = true;
            HeatmapAbsDiff.ShowCoP = false;
            HeatmapAbsDiff.ShowValues = false;
            HeatmapAbsDiff.ShowLegend = false;
            DiffTitle.Text = "絕對差 |A − B|";
            DiffLegend.Text = $"色階：藍=低差異、紅=高差異（max {_result.MaxAbsDiff:F1} g）";
        }

        // ── 容差面板與 OK/NG ──
        TolerancePanel.Visibility = mode == ComparisonMode.ReferenceVsDut ? Visibility.Visible : Visibility.Collapsed;
        OkNgBox.Visibility = mode == ComparisonMode.ReferenceVsDut ? Visibility.Visible : Visibility.Collapsed;
        OkNgMetricPanel.Visibility = mode == ComparisonMode.ReferenceVsDut ? Visibility.Visible : Visibility.Collapsed;
        ChangeMetricPanel.Visibility = mode == ComparisonMode.BeforeAfter ? Visibility.Visible : Visibility.Collapsed;

        if (mode == ComparisonMode.ReferenceVsDut)
        {
            OkNgText.Text = _result.IsPass ? "PASS" : "FAIL";
            OkNgBox.Background = _result.IsPass
                ? (System.Windows.Media.Brush)FindResource("AccentGreen")
                : (System.Windows.Media.Brush)FindResource("AccentRed");
        }

        // ── 通用指標 ──
        MetricMaeText.Text = $"MAE        : {_result.MAE:F3} g";
        MetricRmseText.Text = $"RMSE       : {_result.RMSE:F3} g";
        MetricMaxAbsText.Text = $"Max |Δ|    : {_result.MaxAbsDiff:F2} g";
        MetricPearsonText.Text = $"Pearson R  : {_result.PearsonR:F4}";

        MetricPeakText.Text = $"ΔPeak  : {_result.PeakDelta:+0.0;-0.0;0} g";
        MetricTotalText.Text = $"ΔTotal : {_result.TotalForceDelta:+0.0;-0.0;0} g ({_result.TotalForceDelta / 1000:+0.000;-0.000;0} kg)";
        MetricActiveText.Text = $"ΔActive: {_result.ActivePointsDelta:+0;-0;0} pts";
        MetricCopText.Text = $"CoP shift: {_result.CoPShiftMm:F2} mm";

        // ── Reference-DUT 指標 ──
        if (mode == ComparisonMode.ReferenceVsDut)
        {
            MetricOutOfTolText.Text = $"超容差: {_result.OutOfToleranceCount} pts ({_result.OutOfTolerancePercent:F1}%)";
            MetricToleranceText.Text = $"容差設定: ±{_result.ToleranceGrams:F1}g 或 ±{_result.TolerancePercent:F1}%";
        }

        // ── Before-After 指標 ──
        if (mode == ComparisonMode.BeforeAfter)
        {
            MetricIncreaseText.Text = $"總增加力: +{_result.TotalIncrease:F1} g";
            MetricDecreaseText.Text = $"總減少力: −{_result.TotalDecrease:F1} g";
            MetricNetText.Text = $"淨變化  : {_result.NetChange:+0.0;-0.0;0} g";
        }

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

    private string BuildReportText()
    {
        if (_result == null) return string.Empty;
        var sb = new StringBuilder();
        sb.AppendLine($"Pressure Snapshot Comparison Report");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Mode: {_result.Mode}");
        sb.AppendLine();
        sb.AppendLine("[Snapshot A]");
        sb.AppendLine($"  Captured: {_a.CapturedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  Peak={_a.Peak:F1}g  Total={_a.TotalForce / 1000:F2}kg  Active={_a.ActivePoints}");
        sb.AppendLine();
        sb.AppendLine("[Snapshot B]");
        sb.AppendLine($"  Captured: {_b.CapturedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  Peak={_b.Peak:F1}g  Total={_b.TotalForce / 1000:F2}kg  Active={_b.ActivePoints}");
        sb.AppendLine();
        sb.AppendLine("[Metrics]");
        sb.AppendLine($"  MAE:    {_result.MAE:F3} g");
        sb.AppendLine($"  RMSE:   {_result.RMSE:F3} g");
        sb.AppendLine($"  Max|Δ|: {_result.MaxAbsDiff:F2} g");
        sb.AppendLine($"  PearsonR: {_result.PearsonR:F4}");
        sb.AppendLine($"  ΔPeak:  {_result.PeakDelta:+0.0;-0.0;0} g");
        sb.AppendLine($"  ΔTotal: {_result.TotalForceDelta:+0.0;-0.0;0} g");
        sb.AppendLine($"  ΔActive: {_result.ActivePointsDelta:+0;-0;0} pts");
        sb.AppendLine($"  CoP shift: {_result.CoPShiftMm:F2} mm");

        if (_result.Mode == ComparisonMode.ReferenceVsDut)
        {
            sb.AppendLine();
            sb.AppendLine("[OK/NG]");
            sb.AppendLine($"  Result: {(_result.IsPass ? "PASS" : "FAIL")}");
            sb.AppendLine($"  OutOfTol: {_result.OutOfToleranceCount} pts ({_result.OutOfTolerancePercent:F1}%)");
            sb.AppendLine($"  Tolerance: ±{_result.ToleranceGrams:F1}g 或 ±{_result.TolerancePercent:F1}%");
        }
        if (_result.Mode == ComparisonMode.BeforeAfter)
        {
            sb.AppendLine();
            sb.AppendLine("[Change]");
            sb.AppendLine($"  Total increase: +{_result.TotalIncrease:F1} g");
            sb.AppendLine($"  Total decrease: -{_result.TotalDecrease:F1} g");
            sb.AppendLine($"  Net change:    {_result.NetChange:+0.0;-0.0;0} g");
        }
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
            System.IO.Directory.CreateDirectory(_exportFolder);
            string filename = $"{DateTime.Now:yyyyMMdd_HHmmss}_Comparison.txt";
            string fullPath = System.IO.Path.Combine(_exportFolder, filename);
            System.IO.File.WriteAllText(fullPath, BuildReportText(), Encoding.UTF8);
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
