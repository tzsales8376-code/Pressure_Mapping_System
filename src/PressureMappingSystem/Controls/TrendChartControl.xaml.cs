using System.Windows;
using System.Windows.Controls;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using PressureMappingSystem.Models;

namespace PressureMappingSystem.Controls;

/// <summary>
/// 即時趨勢圖控件：顯示壓力隨時間變化的折線圖
/// 使用 OxyPlot 渲染
/// 顯示：峰值壓力、總壓力、平均壓力、觸發點數
/// </summary>
public partial class TrendChartControl : UserControl
{
    private PlotModel _plotModel;
    private LineSeries _peakSeries;
    private LineSeries _totalSeries;
    private LineSeries _avgSeries;
    private LineSeries _activeSeries;
    private LinearAxis _timeAxis;
    private LinearAxis _pressureAxis;
    private LinearAxis _countAxis;

    private double _timeWindowSeconds = 10.0;
    private readonly object _lock = new();

    // 資料緩衝
    private const int MaxDataPoints = 6000; // 60fps * 100sec

    public TrendChartControl()
    {
        InitializeComponent();
        InitializePlotModel();
    }

    private void InitializePlotModel()
    {
        _plotModel = new PlotModel
        {
            Background = OxyColor.FromRgb(37, 37, 54),
            PlotAreaBorderColor = OxyColor.FromArgb(80, 255, 255, 255),
            TextColor = OxyColor.FromRgb(200, 200, 210),
        };

        // 時間軸 (X)
        _timeAxis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Time (s)",
            TitleColor = OxyColor.FromRgb(180, 180, 190),
            AxislineColor = OxyColor.FromArgb(80, 255, 255, 255),
            TicklineColor = OxyColor.FromArgb(60, 255, 255, 255),
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromArgb(30, 255, 255, 255),
            TextColor = OxyColor.FromRgb(160, 160, 170),
        };
        _plotModel.Axes.Add(_timeAxis);

        // 壓力軸 (Y, 左)
        _pressureAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Pressure (g)",
            TitleColor = OxyColor.FromRgb(180, 180, 190),
            Minimum = 0,
            Maximum = 1100,
            AxislineColor = OxyColor.FromArgb(80, 255, 255, 255),
            TicklineColor = OxyColor.FromArgb(60, 255, 255, 255),
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromArgb(30, 255, 255, 255),
            TextColor = OxyColor.FromRgb(160, 160, 170),
            Key = "pressure"
        };
        _plotModel.Axes.Add(_pressureAxis);

        // 點數軸 (Y, 右)
        _countAxis = new LinearAxis
        {
            Position = AxisPosition.Right,
            Title = "Active Points",
            TitleColor = OxyColor.FromRgb(120, 120, 130),
            Minimum = 0,
            Maximum = 1600,
            AxislineColor = OxyColor.FromArgb(40, 255, 255, 255),
            TextColor = OxyColor.FromRgb(120, 120, 130),
            Key = "count",
            IsAxisVisible = false
        };
        _plotModel.Axes.Add(_countAxis);

        // ── Series ──
        _peakSeries = new LineSeries
        {
            Title = "Peak Pressure",
            Color = OxyColor.FromRgb(237, 125, 49), // TRANZX Orange
            StrokeThickness = 2,
            YAxisKey = "pressure"
        };

        _totalSeries = new LineSeries
        {
            Title = "Total Force (÷10)",
            Color = OxyColor.FromRgb(43, 87, 151), // TRANZX Blue
            StrokeThickness = 1.5,
            YAxisKey = "pressure"
        };

        _avgSeries = new LineSeries
        {
            Title = "Avg Pressure",
            Color = OxyColor.FromRgb(76, 175, 80), // Green
            StrokeThickness = 1.5,
            LineStyle = LineStyle.Dash,
            YAxisKey = "pressure"
        };

        _activeSeries = new LineSeries
        {
            Title = "Active Points",
            Color = OxyColor.FromRgb(150, 150, 170),
            StrokeThickness = 1,
            LineStyle = LineStyle.Dot,
            YAxisKey = "count",
            IsVisible = false
        };

        _plotModel.Series.Add(_peakSeries);
        _plotModel.Series.Add(_totalSeries);
        _plotModel.Series.Add(_avgSeries);
        _plotModel.Series.Add(_activeSeries);

        // Legend
        _plotModel.Legends.Add(new Legend
        {
            LegendPosition = LegendPosition.TopRight,
            LegendBackground = OxyColor.FromArgb(180, 30, 30, 40),
            LegendTextColor = OxyColor.FromRgb(200, 200, 210),
            LegendBorder = OxyColors.Transparent,
            LegendFontSize = 10,
        });

        PlotView.Model = _plotModel;
    }

    /// <summary>
    /// 新增一筆資料點 (由 MainViewModel 呼叫)
    /// </summary>
    public void AddDataPoint(SensorFrame frame)
    {
        double time = frame.TimestampMs / 1000.0;

        lock (_lock)
        {
            _peakSeries.Points.Add(new DataPoint(time, frame.PeakPressureGrams));
            _totalSeries.Points.Add(new DataPoint(time, frame.TotalForceGrams / 10.0)); // scale down
            _avgSeries.Points.Add(new DataPoint(time, frame.AveragePressureGrams));
            _activeSeries.Points.Add(new DataPoint(time, frame.ActivePointCount));

            // 限制資料點數量
            TrimSeries(_peakSeries);
            TrimSeries(_totalSeries);
            TrimSeries(_avgSeries);
            TrimSeries(_activeSeries);

            // 更新時間窗口
            _timeAxis.Minimum = Math.Max(0, time - _timeWindowSeconds);
            _timeAxis.Maximum = time + 0.5;

            // 自動縮放壓力軸
            double maxPressure = 100;
            if (_peakSeries.Points.Count > 0)
                maxPressure = Math.Max(maxPressure, _peakSeries.Points.Max(p => p.Y));
            if (_totalSeries.Points.Count > 0)
                maxPressure = Math.Max(maxPressure, _totalSeries.Points.Max(p => p.Y));
            _pressureAxis.Maximum = maxPressure * 1.15;
        }

        // Throttle UI updates (~30fps for chart)
        Dispatcher.InvokeAsync(() => _plotModel.InvalidatePlot(true),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void TrimSeries(LineSeries series)
    {
        while (series.Points.Count > MaxDataPoints)
            series.Points.RemoveAt(0);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _peakSeries.Points.Clear();
            _totalSeries.Points.Clear();
            _avgSeries.Points.Clear();
            _activeSeries.Points.Clear();
        }
        _plotModel.InvalidatePlot(true);
    }

    // ── UI Event Handlers ──

    private void TimeWindowChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TimeWindowCombo?.SelectedIndex >= 0)
        {
            double[] windows = { 5, 10, 30, 60 };
            _timeWindowSeconds = windows[TimeWindowCombo.SelectedIndex];
        }
    }

    private void OnSeriesToggle(object sender, RoutedEventArgs e)
    {
        if (_peakSeries == null) return;
        _peakSeries.IsVisible = ShowPeakLine?.IsChecked ?? true;
        _totalSeries.IsVisible = ShowTotalLine?.IsChecked ?? true;
        _avgSeries.IsVisible = ShowAvgLine?.IsChecked ?? true;
        _activeSeries.IsVisible = ShowActiveCount?.IsChecked ?? false;
        _countAxis.IsAxisVisible = _activeSeries.IsVisible;
        _plotModel.InvalidatePlot(true);
    }

    private void ClearChart_Click(object sender, RoutedEventArgs e) => Clear();
}
