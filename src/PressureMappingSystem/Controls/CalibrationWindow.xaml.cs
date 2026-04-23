using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PressureMappingSystem.Models;
using PressureMappingSystem.ViewModels;

namespace PressureMappingSystem.Controls;

public partial class CalibrationWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly List<CalibrationRow> _rows = new();

    // 校正砝碼重量 (grams)
    private static readonly (string label, double grams)[] WeightLevels =
    {
        ("0.5 kg",   500),
        ("1 kg",    1000),
        ("2.5 kg",  2500),
        ("5 kg",    5000),
        ("10 kg",  10000),
    };

    public CalibrationWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BuildCalibrationTable();
    }

    private void BuildCalibrationTable()
    {
        CalRows.Children.Clear();
        _rows.Clear();

        foreach (var (label, grams) in WeightLevels)
        {
            var row = new CalibrationRow { WeightLabel = label, WeightGrams = grams };

            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Weight label
            var lblWeight = new TextBlock
            {
                Text = label,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lblWeight, 0);
            grid.Children.Add(lblWeight);

            // Capture button — 使用 Style 而非 ControlTemplate
            var btnCapture = new Button
            {
                Content = "擷取",
                Style = (Style)FindResource("CaptureBtn"),
                Tag = row
            };
            btnCapture.Click += CaptureRow_Click;
            Grid.SetColumn(btnCapture, 1);
            grid.Children.Add(btnCapture);

            // Pre-weighted value (readonly, captured from sensor)
            row.PreValueBox = new TextBox
            {
                Text = "---",
                IsReadOnly = true,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(8, 0, 4, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x3D)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x44)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF))
            };
            Grid.SetColumn(row.PreValueBox, 2);
            grid.Children.Add(row.PreValueBox);

            // Post-weighted value (editable, shows calibrated result)
            row.PostValueBox = new TextBox
            {
                Text = "---",
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x3D)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0xDD, 0x88)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF))
            };
            Grid.SetColumn(row.PostValueBox, 3);
            grid.Children.Add(row.PostValueBox);

            CalRows.Children.Add(grid);
            _rows.Add(row);
        }
    }

    private void CaptureRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CalibrationRow row)
        {
            double avgReading = _vm.GetCurrentAverageReading();
            double peakReading = _vm.GetCurrentPeakReading();

            if (avgReading <= 0 && peakReading <= 0)
            {
                StatusLabel.Text = "無有效感測器數據 — 請先連接感測器並施加壓力";
                return;
            }

            // 擷取: 使用平均值作為加權前數值
            row.PreValue = avgReading;
            row.PreValueBox!.Text = $"{avgReading:F1}";
            StatusLabel.Text = $"已擷取 {row.WeightLabel}: 平均 {avgReading:F1}, Peak {peakReading:F1}";
        }
    }

    private void Calculate_Click(object sender, RoutedEventArgs e)
    {
        // 計算校正: 根據已知砝碼重量和感測器讀值，生成加權後數值
        int filled = 0;
        foreach (var row in _rows)
        {
            if (row.PreValue > 0)
            {
                // 加權後數值 = 實際砝碼重量 (grams)
                row.PostValue = row.WeightGrams;
                row.PostValueBox!.Text = $"{row.WeightGrams:F0}";
                filled++;
            }
        }

        if (filled == 0)
        {
            StatusLabel.Text = "請先至少擷取一個砝碼的讀值";
            return;
        }

        StatusLabel.Text = $"已計算 {filled} 個校正點 — 加權後數值已填入（可手動修改），確認後請按「套用校正」";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var calPoints = new List<(double weightGrams, double preValue, double postValue)>();

        foreach (var row in _rows)
        {
            if (row.PreValue <= 0) continue;

            // 讀取使用者可能手動修改的加權後數值
            if (double.TryParse(row.PostValueBox!.Text, out double postVal) && postVal > 0)
            {
                row.PostValue = postVal;
                calPoints.Add((row.WeightGrams, row.PreValue, row.PostValue));
            }
        }

        if (calPoints.Count == 0)
        {
            StatusLabel.Text = "沒有有效的校正資料 — 請先擷取並計算";
            return;
        }

        _vm.ApplyCustomCalibration(calPoints);
        StatusLabel.Text = $"校正已套用！({calPoints.Count} 個校正點)";
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            row.PreValue = 0;
            row.PostValue = 0;
            row.PreValueBox!.Text = "---";
            row.PostValueBox!.Text = "---";
        }
        _vm.ApplyCustomCalibration(new());
        StatusLabel.Text = "已清除全部校正資料";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // 收集有效校正資料
        var calPoints = new List<CalibrationDataPoint>();
        foreach (var row in _rows)
        {
            if (row.PreValue <= 0) continue;
            if (double.TryParse(row.PostValueBox!.Text, out double postVal) && postVal > 0)
            {
                calPoints.Add(new CalibrationDataPoint
                {
                    WeightLabel = row.WeightLabel,
                    WeightGrams = row.WeightGrams,
                    PreValue = row.PreValue,
                    PostValue = postVal
                });
            }
        }

        if (calPoints.Count == 0)
        {
            StatusLabel.Text = "沒有有效的校正資料 — 請先擷取並計算";
            return;
        }

        // 彈出命名對話框
        var nameDialog = new Window
        {
            Title = "儲存校正設定檔",
            Width = 380, Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E))
        };

        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new TextBlock
        {
            Text = "請輸入校正設定檔名稱:",
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            FontSize = 14, Margin = new Thickness(0, 0, 0, 8)
        });

        var nameBox = new TextBox
        {
            Text = $"Cal-{DateTime.Now:yyyyMMdd-HHmm}",
            FontSize = 14, Padding = new Thickness(6, 4, 6, 4),
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x3D)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF))
        };
        sp.Children.Add(nameBox);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var btnOk = new Button
        {
            Content = "儲存",
            Width = 80, Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(4)
        };
        var btnCancel = new Button
        {
            Content = "取消",
            Width = 80, Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(4)
        };
        btnOk.Click += (_, _) => { nameDialog.DialogResult = true; };
        btnCancel.Click += (_, _) => { nameDialog.DialogResult = false; };
        btnPanel.Children.Add(btnOk);
        btnPanel.Children.Add(btnCancel);
        sp.Children.Add(btnPanel);
        nameDialog.Content = sp;

        if (nameDialog.ShowDialog() != true) return;

        var profileName = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(profileName))
        {
            StatusLabel.Text = "名稱不能為空";
            return;
        }

        var profile = new CustomCalibrationProfile
        {
            Name = profileName,
            Description = $"{calPoints.Count} 個校正點",
            CreatedAt = DateTime.Now,
            Points = calPoints
        };

        _vm.SaveCalibrationProfile(profile);
        StatusLabel.Text = $"校正設定已儲存: {profileName}";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 內部資料結構：每一列校正資料
    /// </summary>
    private class CalibrationRow
    {
        public string WeightLabel { get; set; } = "";
        public double WeightGrams { get; set; }
        public double PreValue { get; set; }
        public double PostValue { get; set; }
        public TextBox? PreValueBox { get; set; }
        public TextBox? PostValueBox { get; set; }
    }
}
