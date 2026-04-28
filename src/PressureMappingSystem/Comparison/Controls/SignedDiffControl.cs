using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;

namespace PressureMappingSystem.Comparison.Controls;

/// <summary>
/// 顯示「有號差值熱力圖」— 紅 = 正（A 比 B 大）、藍 = 負（A 比 B 小）、白 = 0。
/// 用於 Before-After 模式的視覺呈現。
/// 採用簡單 WriteableBitmap，不依賴 SkiaSharp，與主程式 HeatmapControl 完全獨立。
/// </summary>
public class SignedDiffControl : Image
{
    private WriteableBitmap? _bitmap;

    public static readonly DependencyProperty SignedDiffProperty =
        DependencyProperty.Register(nameof(SignedDiff), typeof(double[,]),
            typeof(SignedDiffControl),
            new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty SymmetricMaxProperty =
        DependencyProperty.Register(nameof(SymmetricMax), typeof(double),
            typeof(SignedDiffControl),
            new PropertyMetadata(0.0, OnChanged));

    /// <summary>40×40 有號差值矩陣</summary>
    public double[,]? SignedDiff
    {
        get => (double[,]?)GetValue(SignedDiffProperty);
        set => SetValue(SignedDiffProperty, value);
    }

    /// <summary>對稱色階的範圍（±SymmetricMax）。0 代表自動以資料範圍決定</summary>
    public double SymmetricMax
    {
        get => (double)GetValue(SymmetricMaxProperty);
        set => SetValue(SymmetricMaxProperty, value);
    }

    public SignedDiffControl()
    {
        Stretch = Stretch.Fill;
        SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SignedDiffControl ctrl) ctrl.Render();
    }

    private void Render()
    {
        var data = SignedDiff;
        if (data == null) return;
        int rows = data.GetLength(0);
        int cols = data.GetLength(1);

        if (_bitmap == null || _bitmap.PixelWidth != cols || _bitmap.PixelHeight != rows)
        {
            _bitmap = new WriteableBitmap(cols, rows, 96, 96, PixelFormats.Bgra32, null);
            Source = _bitmap;
        }

        // 決定對稱範圍
        double vmax = SymmetricMax;
        if (vmax <= 0)
        {
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    double abs = Math.Abs(data[r, c]);
                    if (abs > vmax) vmax = abs;
                }
            if (vmax < 0.001) vmax = 1.0;
        }

        var pixels = new byte[cols * rows * 4];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                double v = data[r, c];
                double n = Math.Clamp(v / vmax, -1.0, 1.0);
                var (R, G, B) = DivergentColor(n);
                int i = (r * cols + c) * 4;
                pixels[i + 0] = B;
                pixels[i + 1] = G;
                pixels[i + 2] = R;
                pixels[i + 3] = 255;
            }
        _bitmap.WritePixels(new Int32Rect(0, 0, cols, rows), pixels, cols * 4, 0);
    }

    /// <summary>
    /// Divergent colormap：
    ///   n = -1 → 深藍 (0, 50, 200)
    ///   n =  0 → 白   (255, 255, 255)
    ///   n = +1 → 深紅 (200, 30, 30)
    /// </summary>
    private static (byte R, byte G, byte B) DivergentColor(double n)
    {
        if (n >= 0)
        {
            double t = n;  // 0..1
            byte r = (byte)(255 - t * 55);   // 255 → 200
            byte g = (byte)(255 - t * 225);  // 255 → 30
            byte b = (byte)(255 - t * 225);  // 255 → 30
            return (r, g, b);
        }
        else
        {
            double t = -n;
            byte r = (byte)(255 - t * 255);
            byte g = (byte)(255 - t * 205);  // 255 → 50
            byte b = (byte)(255 - t * 55);   // 255 → 200
            return (r, g, b);
        }
    }
}
