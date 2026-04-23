using System.Windows;
using PressureMappingSystem.Models;
using PressureMappingSystem.ViewModels;

namespace PressureMappingSystem.Controls;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _vm;

    // 全域 kg → 單點 g 換算
    // totalKg * 1000g / 1600 points = grams per point
    private static double KgToPerPointGrams(double totalKg)
        => totalKg * 1000.0 / (SensorConfig.Rows * SensorConfig.Cols);

    private static double PerPointGramsToKg(double gramsPerPoint)
        => gramsPerPoint * (SensorConfig.Rows * SensorConfig.Cols) / 1000.0;

    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        // 把目前 ViewModel 的 per-point grams 轉回 kg 顯示
        double currentKg = PerPointGramsToKg(_vm.DisplayThreshold);
        ThresholdSlider.Value = currentKg;
        UpdateLabels(currentKg);

        // 最低感測門檻滑桿初始化
        NoiseFloorSlider.Value = _vm.NoiseFloorGrams;
        NoiseFloorValueText.Text = _vm.NoiseFloorGrams.ToString("F0");

        // 訊號純淨度滑桿初始化
        NoiseFilterSlider.Value = _vm.NoiseFilterPercent;
        UpdateNoiseFilterLabels(_vm.NoiseFilterPercent);
    }

    private void ThresholdSlider_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_vm == null) return;

        double totalKg = e.NewValue;
        double perPointG = KgToPerPointGrams(totalKg);
        _vm.DisplayThreshold = perPointG;
        UpdateLabels(totalKg);
    }

    private void UpdateLabels(double totalKg)
    {
        double perPointG = KgToPerPointGrams(totalKg);
        ThresholdValueText.Text = totalKg.ToString("F0");
        PerPointText.Text = $"(Per point: {perPointG:F1} g)";
    }

    private void NoiseFloorSlider_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_vm == null) return;
        _vm.NoiseFloorGrams = e.NewValue;
        if (NoiseFloorValueText != null)
            NoiseFloorValueText.Text = e.NewValue.ToString("F0");
    }

    private void NoiseFilterSlider_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_vm == null) return;

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
            NoiseFilterDescText.Text = "(未啟用)";
        }
        else
        {
            int dLevel = (int)(100 - percent); // 5% → D95, 10% → D90
            NoiseFilterDLevelText.Text = dLevel.ToString();

            // 以峰值 100g 為範例計算門檻（含 ×3 放大係數）
            double exThreshold = 100 * (percent / 100.0) * 3.0;
            NoiseFilterDescText.Text =
                $"(門檻=峰值×{percent:F0}%×3 | 例：峰值 100g → 門檻 {exThreshold:F1}g + 邊緣侵蝕)";
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        ThresholdSlider.Value = 1600; // 滿量程 1600 kg total (= 1000 g/point)
        NoiseFloorSlider.Value = 5;   // 最低感測門檻重置為規格值
        NoiseFilterSlider.Value = 0;  // 訊號純淨度重置
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
