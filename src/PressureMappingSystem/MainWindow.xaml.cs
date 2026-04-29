using System.Windows;
using System.Windows.Controls;
using PressureMappingSystem.Services;
using PressureMappingSystem.ViewModels;

namespace PressureMappingSystem;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        _viewModel.TrendChart = TrendChartView;
        DataContext = _viewModel;

        HeatmapView.PointHovered += (_, info) =>
            _viewModel.OnPointHovered(info.row, info.col, info.pressure);

        // Wire ClearRoiCommand to also clear the heatmap control's multi-ROI list
        _viewModel.ClearRoiRequested += () => HeatmapView.ClearAllRois();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _viewModel?.Dispose();
    }

    private void SimModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;
        if (sender is ComboBox combo && combo.SelectedIndex >= 0)
            _viewModel.SelectedSimMode = (SimulationMode)combo.SelectedIndex;
    }

    private void SpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;
        if (sender is ComboBox combo && combo.SelectedIndex >= 0)
        {
            double[] speeds = { 0.25, 0.5, 1.0, 2.0, 4.0 };
            _viewModel.PlaybackSpeed = speeds[combo.SelectedIndex];
        }
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;
        if (sender is ComboBox combo && combo.SelectedIndex >= 0)
        {
            string[] codes = { "zh-TW", "en-US", "ja-JP" };
            _viewModel.SelectedLanguage = codes[combo.SelectedIndex];
        }
    }

    private void InterpCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;
        if (sender is ComboBox combo && combo.SelectedIndex >= 0)
        {
            int[] levels = { 1, 3, 5 };
            _viewModel.InterpolationLevel = levels[combo.SelectedIndex];
        }
    }
}
