using System.Windows;
using System.Windows.Controls;
using PressureMappingSystem.Models;
using PressureMappingSystem.Services;
using PressureMappingSystem.ViewModels;

namespace PressureMappingSystem.Controls;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _initializing = true;

    // 全域 kg → 單點 g 換算
    private static double KgToPerPointGrams(double totalKg)
        => totalKg * 1000.0 / (SensorConfig.Rows * SensorConfig.Cols);

    private static double PerPointGramsToKg(double gramsPerPoint)
        => gramsPerPoint * (SensorConfig.Rows * SensorConfig.Cols) / 1000.0;

    public LocalizationService L => LocalizationService.Instance;

    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = this;

        // Display Range
        double currentKg = PerPointGramsToKg(_vm.DisplayThreshold);
        ThresholdSlider.Value = currentKg;
        UpdateLabels(currentKg);

        // Noise Floor
        NoiseFloorSlider.Value = _vm.NoiseFloorGrams;
        NoiseFloorValueText.Text = _vm.NoiseFloorGrams.ToString("F0");

        // Signal Purity
        NoiseFilterSlider.Value = _vm.NoiseFilterPercent;
        UpdateNoiseFilterLabels(_vm.NoiseFilterPercent);

        // Filter Mode
        FilterModeCombo.SelectedIndex = (int)_vm.FilterMode;
        TcfResponseSlider.Value = _vm.TcfResponseMs;
        TcfNoiseSupSlider.Value = _vm.TcfNoiseSuppression;
        TcfResponseText.Text = _vm.TcfResponseMs.ToString("F0");
        TcfNoiseSupText.Text = _vm.TcfNoiseSuppression.ToString("F0");
        UpdateTcfPanelVisibility();

        _initializing = false;
    }

    // ── Filter Mode ──
    private void FilterModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _vm == null) return;
        _vm.FilterMode = (FilterMode)FilterModeCombo.SelectedIndex;
        UpdateTcfPanelVisibility();
    }

    private void UpdateTcfPanelVisibility()
    {
        if (TcfParamsPanel == null) return;
        TcfParamsPanel.Visibility = FilterModeCombo.SelectedIndex == (int)FilterMode.TemporalCoherence
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TcfResponseSlider_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || _vm == null) return;
        _vm.TcfResponseMs = e.NewValue;
        if (TcfResponseText != null)
            TcfResponseText.Text = e.NewValue.ToString("F0");
    }

    private void TcfNoiseSupSlider_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || _vm == null) return;
        _vm.TcfNoiseSuppression = e.NewValue;
        if (TcfNoiseSupText != null)
            TcfNoiseSupText.Text = e.NewValue.ToString("F0");
    }

    // ── Display Range ──
    private void ThresholdSlider_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || _vm == null) return;
        double totalKg = e.NewValue;
        double perPointG = KgToPerPointGrams(totalKg);
        _vm.DisplayThreshold = perPointG;
        UpdateLabels(totalKg);
    }

    private void UpdateLabels(double totalKg)
    {
        double perPointG = KgToPerPointGrams(totalKg);
        ThresholdValueText.Text = totalKg.ToString("F0");
        PerPointText.Text = $"({LocalizationService.T("Settings.PerPoint")} {perPointG:F1} g)";
    }

    // ── Noise Floor ──
    private void NoiseFloorSlider_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || _vm == null) return;
        _vm.NoiseFloorGrams = e.NewValue;
        if (NoiseFloorValueText != null)
            NoiseFloorValueText.Text = e.NewValue.ToString("F0");
    }

    // ── Signal Purity ──
    private void NoiseFilterSlider_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing || _vm == null) return;
        double percent = e.NewValue;
        _vm.NoiseFilterPercent = percent;
        UpdateNoiseFilterLabels(percent);
    }

    private void UpdateNoiseFilterLabels(double percent)
    {
        if (NoiseFilterValueText == null) return;
        NoiseFilterValueText.Text = percent.ToString("F1");

        if (percent <= 0)
        {
            NoiseFilterDLevelText.Text = "—";
            NoiseFilterDescText.Text = LocalizationService.T("Settings.PurityOff");
        }
        else
        {
            int dLevel = (int)(100 - percent);
            NoiseFilterDLevelText.Text = dLevel.ToString();
            double exThreshold = 100 * (percent / 100.0) * 3.0;
            NoiseFilterDescText.Text =
                $"(門檻=峰值×{percent:F0}%×3 | 例：峰值 100g → 門檻 {exThreshold:F1}g + 邊緣侵蝕)";
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        ThresholdSlider.Value = 1600;
        NoiseFloorSlider.Value = 5;
        NoiseFilterSlider.Value = 0;
        FilterModeCombo.SelectedIndex = 0;  // Legacy
        TcfResponseSlider.Value = 80;
        TcfNoiseSupSlider.Value = 50;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = new UserSettings
        {
            FilterMode = (int)_vm.FilterMode,
            TcfResponseMs = _vm.TcfResponseMs,
            TcfNoiseSuppression = _vm.TcfNoiseSuppression,
            DisplayThresholdGrams = _vm.DisplayThreshold,
            NoiseFloorGrams = _vm.NoiseFloorGrams,
            NoiseFilterPercent = _vm.NoiseFilterPercent,
        };
        settings.Save();
        MessageBox.Show("設定已儲存，下次啟動時將自動載入。", "儲存成功",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
