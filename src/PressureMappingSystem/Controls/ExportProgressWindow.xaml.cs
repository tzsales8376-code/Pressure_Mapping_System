using System.Windows;

namespace PressureMappingSystem.Controls;

public partial class ExportProgressWindow : Window
{
    public ExportProgressWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 更新進度 (0.0 ~ 1.0)
    /// </summary>
    public void UpdateProgress(double fraction, string? message = null)
    {
        Dispatcher.Invoke(() =>
        {
            ProgressBar.Value = fraction * 100;
            PercentText.Text = $"{fraction:P0}";
            if (message != null)
                StatusText.Text = message;
        });
    }

    /// <summary>
    /// 匯出完成，自動關閉
    /// </summary>
    public void Complete(string resultMessage)
    {
        Dispatcher.Invoke(() =>
        {
            ProgressBar.Value = 100;
            PercentText.Text = "100%";
            StatusText.Text = resultMessage;
            DialogResult = true;
            Close();
        });
    }

    /// <summary>
    /// 匯出失敗
    /// </summary>
    public void Fail(string errorMessage)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = errorMessage;
            PercentText.Text = "失敗";
            ProgressBar.Foreground = System.Windows.Media.Brushes.Red;
            // 2 秒後自動關閉
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += (_, _) => { timer.Stop(); DialogResult = false; Close(); };
            timer.Start();
        });
    }
}
