using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Newtonsoft.Json;
using PressureMappingSystem.Models;
using PressureMappingSystem.Services;
using PressureMappingSystem.ViewModels;

namespace PressureMappingSystem.Controls;

public partial class CalibrationWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ObservableCollection<CalRowVm> _rows = new();
    private CalibrationFitter.FitResult? _lastFitResult;

    public CalibrationWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        // 載入預設：先給 3 列空白讓使用者馬上可擷取
        for (int i = 0; i < 3; i++) AddRow(((i + 1) * 1000).ToString(), "");
        RenderRows();

        StatusText.Text = "準備就緒。放上砝碼後按該列的「擷取」按鈕記錄 raw sum。";
    }

    // ─── Row helpers ───
    private class CalRowVm
    {
        public string Label = "";
        public double WeightG;
        public double RawSum;
        public int ActiveCells;
        public bool IsEnabled = true;
        public double FittedG = double.NaN;
        public double ResidualPct = double.NaN;
        public bool HasCapture => RawSum > 0;
    }

    private void AddRow(string weightText, string label)
    {
        double.TryParse(weightText, out var w);
        _rows.Add(new CalRowVm { WeightG = w, Label = label });
    }

    private void RenderRows()
    {
        RowsPanel.Children.Clear();
        for (int i = 0; i < _rows.Count; i++)
        {
            var rowData = _rows[i];
            int idx = i;  // capture

            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1), Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x36)) };
            foreach (var w in new[] { 40, 40, 100, 110, 120, 90, 110, 90 })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // # index
            var idxBlock = new TextBlock
            {
                Text = (idx + 1).ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0xA8)),
            };
            Grid.SetColumn(idxBlock, 0); grid.Children.Add(idxBlock);

            // enable checkbox
            var cb = new CheckBox
            {
                IsChecked = rowData.IsEnabled,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            cb.Checked += (_, _) => { rowData.IsEnabled = true; };
            cb.Unchecked += (_, _) => { rowData.IsEnabled = false; };
            Grid.SetColumn(cb, 1); grid.Children.Add(cb);

            // label textbox
            var labelBox = new TextBox
            {
                Text = rowData.Label,
                Margin = new Thickness(4, 2, 4, 2),
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x3D)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            };
            labelBox.TextChanged += (_, _) => { rowData.Label = labelBox.Text; };
            Grid.SetColumn(labelBox, 2); grid.Children.Add(labelBox);

            // weight textbox
            var weightBox = new TextBox
            {
                Text = rowData.WeightG > 0 ? rowData.WeightG.ToString("F0") : "",
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(4, 2, 4, 2),
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x3D)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            };
            weightBox.LostFocus += (_, _) =>
            {
                double.TryParse(weightBox.Text, out var w);
                rowData.WeightG = w;
            };
            Grid.SetColumn(weightBox, 3); grid.Children.Add(weightBox);

            // Raw sum (readonly)
            var rawBlock = new TextBlock
            {
                Text = rowData.HasCapture ? rowData.RawSum.ToString("F0") : "---",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 8, 0),
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x44)),
            };
            Grid.SetColumn(rawBlock, 4); grid.Children.Add(rawBlock);

            // active cells
            var activeBlock = new TextBlock
            {
                Text = rowData.HasCapture ? rowData.ActiveCells.ToString() : "---",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0xA8)),
                FontFamily = new FontFamily("Consolas"),
            };
            Grid.SetColumn(activeBlock, 5); grid.Children.Add(activeBlock);

            // fitted
            var fittedBlock = new TextBlock
            {
                Text = double.IsNaN(rowData.FittedG) ? "---" : rowData.FittedG.ToString("F1"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 8, 0),
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0xDD, 0x88)),
            };
            Grid.SetColumn(fittedBlock, 6); grid.Children.Add(fittedBlock);

            // residual
            var resBlock = new TextBlock
            {
                Text = double.IsNaN(rowData.ResidualPct) ? "---" : $"{rowData.ResidualPct:+0.0;-0.0}%",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas"),
            };
            if (!double.IsNaN(rowData.ResidualPct))
            {
                var abs = Math.Abs(rowData.ResidualPct);
                resBlock.Foreground = abs < 5
                    ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                    : abs < 15
                        ? new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x44))
                        : new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
            }
            Grid.SetColumn(resBlock, 7); grid.Children.Add(resBlock);

            // Actions: Capture / Delete
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 2, 2) };
            var captureBtn = new Button
            {
                Content = "擷取",
                Margin = new Thickness(2),
                Padding = new Thickness(8, 2, 8, 2),
                Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            captureBtn.Click += (_, _) => OnCapture(idx);
            actions.Children.Add(captureBtn);

            var deleteBtn = new Button
            {
                Content = "✕",
                Margin = new Thickness(2),
                Padding = new Thickness(8, 2, 8, 2),
                Background = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x66)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            deleteBtn.Click += (_, _) => OnDelete(idx);
            actions.Children.Add(deleteBtn);

            Grid.SetColumn(actions, 8); grid.Children.Add(actions);

            RowsPanel.Children.Add(grid);
        }
    }

    private void OnCapture(int idx)
    {
        if (idx < 0 || idx >= _rows.Count) return;
        var row = _rows[idx];

        // 擷取前暫停當前 profile，確保讀到的是「未校正」的 raw
        _vm.SuspendCalibrationForCapture();
        try
        {
            var (rawSum, active) = _vm.GetCurrentRawReading();
            if (rawSum <= 0)
            {
                StatusText.Text = "讀到的 raw sum = 0。請確認感測器已連接、歸零，且砝碼已放上。";
                return;
            }
            row.RawSum = rawSum;
            row.ActiveCells = active;
            StatusText.Text = $"已擷取列 {idx + 1}: rawSum = {rawSum:F0}, active cells = {active}";
        }
        finally
        {
            _vm.ResumeCalibrationAfterCapture();
        }

        // 擷取會改變其他列的擬合結果，清掉上次擬合
        _lastFitResult = null;
        foreach (var r in _rows) { r.FittedG = double.NaN; r.ResidualPct = double.NaN; }
        FitResultText.Text = "(擷取後需重新計算擬合)";
        FitDetailText.Text = "";
        RenderRows();
    }

    private void OnDelete(int idx)
    {
        if (idx < 0 || idx >= _rows.Count) return;
        _rows.RemoveAt(idx);
        _lastFitResult = null;
        RenderRows();
    }

    // ─── Buttons ───
    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
        AddRow(NewWeightBox.Text, "");
        RenderRows();
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        _rows.Clear();
        _lastFitResult = null;
        FitResultText.Text = "(尚未擬合)";
        FitDetailText.Text = "";
        RenderRows();
        StatusText.Text = "已清除全部校正資料";
    }

    private void Calculate_Click(object sender, RoutedEventArgs e)
    {
        var activePoints = _rows
            .Where(r => r.IsEnabled && r.HasCapture && r.WeightG > 0)
            .Select(r => (r.RawSum, r.WeightG))
            .ToList();

        if (activePoints.Count < 2)
        {
            StatusText.Text = "需要至少 2 個已擷取且啟用的校正點才能擬合";
            return;
        }

        var method = FitMethodCombo.SelectedIndex == 0
            ? CalibrationFitMethod.Linear
            : CalibrationFitMethod.PowerLaw;

        var fit = CalibrationFitter.Fit(activePoints, method);
        _lastFitResult = fit;

        if (!fit.IsValid)
        {
            FitResultText.Text = "擬合失敗：" + fit.Message;
            FitResultText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
            return;
        }

        FitResultText.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        FitResultText.Text = fit.Message;

        // 把擬合的 fitted 值與殘差寫回每一列（含 disabled 的，讓使用者看到停用的點會偏多少）
        int ridx = 0;
        foreach (var r in _rows)
        {
            if (r.HasCapture && r.WeightG > 0)
            {
                if (r.IsEnabled && ridx < fit.Residuals.Length)
                {
                    var profile = new CustomCalibrationProfile
                    {
                        FitMethod = fit.Method,
                        FitParameters = fit.Parameters,
                    };
                    r.FittedG = profile.ApplyCurve(r.RawSum);
                    r.ResidualPct = fit.Residuals[ridx];
                    ridx++;
                }
                else
                {
                    // 停用的點不參與擬合，仍可顯示「如果用當前曲線預測會差多少」
                    var profile = new CustomCalibrationProfile
                    {
                        FitMethod = fit.Method,
                        FitParameters = fit.Parameters,
                    };
                    r.FittedG = profile.ApplyCurve(r.RawSum);
                    r.ResidualPct = r.WeightG > 0 ? (r.FittedG - r.WeightG) / r.WeightG * 100.0 : double.NaN;
                }
            }
        }

        double maxAbs = fit.Residuals.Where(x => !double.IsNaN(x)).Select(Math.Abs).DefaultIfEmpty(0).Max();
        FitDetailText.Text = $"使用 {activePoints.Count} 個點擬合 · 最大殘差 {maxAbs:F1}%";

        RenderRows();
        StatusText.Text = $"擬合完成。{(fit.RSquared > 0.99 ? "品質良好" : fit.RSquared > 0.95 ? "品質尚可" : "品質偏差，建議增加或停用偏移點")}";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_lastFitResult == null || !_lastFitResult.IsValid)
        {
            StatusText.Text = "請先「計算擬合」";
            return;
        }

        var profile = BuildProfile(isTempName: true);
        _vm.ApplyCalibrationProfile(profile);
        StatusText.Text = $"校正曲線已套用到主程式（尚未儲存，關閉後會遺失）";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_lastFitResult == null || !_lastFitResult.IsValid)
        {
            StatusText.Text = "請先「計算擬合」";
            return;
        }

        var nameDialog = new Window
        {
            Title = "儲存校正設定檔",
            Width = 400, Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E)),
        };
        var sp = new StackPanel { Margin = new Thickness(16) };

        sp.Children.Add(new TextBlock
        {
            Text = "校正檔名稱:", FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            Margin = new Thickness(0, 0, 0, 4),
        });
        var nameBox = new TextBox
        {
            Text = $"Cal-{DateTime.Now:yyyyMMdd-HHmm}",
            FontSize = 13, Padding = new Thickness(6, 4, 6, 4),
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x3D)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
        };
        sp.Children.Add(nameBox);

        sp.Children.Add(new TextBlock
        {
            Text = "操作員 (選填):", FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            Margin = new Thickness(0, 8, 0, 4),
        });
        var opBox = new TextBox
        {
            FontSize = 13, Padding = new Thickness(6, 4, 6, 4),
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x3D)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
        };
        sp.Children.Add(opBox);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "儲存", Width = 80, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(4),
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
        var cancel = new Button { Content = "取消", Width = 80, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(4),
            Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)), Foreground = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(0) };
        ok.Click += (_, _) => nameDialog.DialogResult = true;
        cancel.Click += (_, _) => nameDialog.DialogResult = false;
        btnPanel.Children.Add(ok);
        btnPanel.Children.Add(cancel);
        sp.Children.Add(btnPanel);
        nameDialog.Content = sp;

        if (nameDialog.ShowDialog() != true) return;
        var name = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText.Text = "名稱不可為空";
            return;
        }

        var profile = BuildProfile(isTempName: false);
        profile.Name = name;
        profile.Operator = opBox.Text.Trim();

        _vm.SaveCalibrationProfile(profile);
        _vm.ApplyCalibrationProfile(profile);
        StatusText.Text = $"已儲存並套用: {name}";
    }

    private void LoadFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "校正檔 (*.json)|*.json",
            Title = "載入校正檔",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var profile = JsonConvert.DeserializeObject<CustomCalibrationProfile>(json);
            if (profile == null || profile.Points.Count == 0)
            {
                StatusText.Text = "檔案無有效校正點";
                return;
            }

            // 載入到 rows
            _rows.Clear();
            foreach (var pt in profile.Points)
            {
                _rows.Add(new CalRowVm
                {
                    Label = pt.WeightLabel,
                    WeightG = pt.WeightGrams,
                    RawSum = pt.RawSum > 0 ? pt.RawSum : pt.PreValue,  // v1 fallback
                    ActiveCells = pt.ActiveCells,
                    IsEnabled = pt.IsEnabled,
                    FittedG = double.NaN,
                    ResidualPct = double.NaN,
                });
            }

            // 如果原本有擬合就設回來
            if (profile.HasFit)
            {
                FitMethodCombo.SelectedIndex = (int)profile.FitMethod;
                _lastFitResult = new CalibrationFitter.FitResult
                {
                    Method = profile.FitMethod,
                    Parameters = profile.FitParameters,
                    RSquared = profile.RSquared,
                    IsValid = true,
                    Message = profile.FitMethod == CalibrationFitMethod.Linear
                        ? $"Linear: W = {profile.FitParameters[0]:G4}·raw + {profile.FitParameters[1]:G4}  (R²={profile.RSquared:F4})"
                        : $"PowerLaw: W = {profile.FitParameters[0]:G4}·raw^{profile.FitParameters[1]:F4}  (R²={profile.RSquared:F4})",
                };
                FitResultText.Text = _lastFitResult.Message;
                FitDetailText.Text = $"由檔案載入 · {_rows.Count} 個點";
            }
            else
            {
                _lastFitResult = null;
                FitResultText.Text = "(舊檔無擬合曲線，請按「計算擬合」)";
                FitDetailText.Text = "";
            }

            RenderRows();
            StatusText.Text = $"已載入 {profile.Points.Count} 個校正點 from {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "載入失敗: " + ex.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ─── 建立 profile ───
    private CustomCalibrationProfile BuildProfile(bool isTempName)
    {
        if (_lastFitResult == null || !_lastFitResult.IsValid)
            throw new InvalidOperationException("無擬合結果");

        var points = new List<CalibrationDataPoint>();
        int ridx = 0;
        foreach (var r in _rows)
        {
            if (!r.HasCapture || r.WeightG <= 0) continue;

            var pt = new CalibrationDataPoint
            {
                WeightLabel = string.IsNullOrEmpty(r.Label) ? $"{r.WeightG:F0}g" : r.Label,
                WeightGrams = r.WeightG,
                PreValue = r.RawSum,     // v1 相容：把 rawSum 放 PreValue
                PostValue = r.WeightG,   // v1 相容：PostValue = 實際重量
                RawSum = r.RawSum,
                ActiveCells = r.ActiveCells,
                IsEnabled = r.IsEnabled,
                ResidualPercent = r.IsEnabled && ridx < _lastFitResult.Residuals.Length
                    ? _lastFitResult.Residuals[ridx] : double.NaN,
            };
            if (r.IsEnabled) ridx++;
            points.Add(pt);
        }

        return new CustomCalibrationProfile
        {
            Name = isTempName ? $"Temp-{DateTime.Now:HHmmss}" : "",
            Description = $"{points.Count} 個校正點 · {_lastFitResult.Method}",
            CreatedAt = DateTime.Now,
            Points = points,
            FitMethod = _lastFitResult.Method,
            FitParameters = _lastFitResult.Parameters,
            RSquared = _lastFitResult.RSquared,
        };
    }
}
