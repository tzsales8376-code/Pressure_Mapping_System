using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PressureMappingSystem.Models;
using PressureMappingSystem.Services;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace PressureMappingSystem.Controls;

/// <summary>
/// 高效能即時熱力圖控件，使用 SkiaSharp 渲染
/// 白底、黑字、256 階色階、40×40 格線、ROI 框選放大
/// </summary>
public class HeatmapControl : SKElement
{
    // ── CJK 字型支援（中日韓文字渲染）──
    // Windows 預裝「微軟正黑體」支援繁中/簡中/日文/韓文
    // 若系統無此字型則回退至 SKTypeface.Default
    private static readonly SKTypeface CjkTypeface =
        SKTypeface.FromFamilyName("Microsoft JhengHei") ?? SKTypeface.Default;
    private static readonly SKTypeface CjkBoldTypeface =
        SKTypeface.FromFamilyName("Microsoft JhengHei", SKFontStyleWeight.Bold,
            SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? CjkTypeface;

    // ── Dependency Properties ──
    public static readonly DependencyProperty PressureDataProperty =
        DependencyProperty.Register(nameof(PressureData), typeof(double[,]), typeof(HeatmapControl),
            new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty DisplayModeProperty =
        DependencyProperty.Register(nameof(DisplayMode), typeof(HeatmapDisplayMode), typeof(HeatmapControl),
            new PropertyMetadata(HeatmapDisplayMode.Heatmap, OnDataChanged));

    public static readonly DependencyProperty ShowGridProperty =
        DependencyProperty.Register(nameof(ShowGrid), typeof(bool), typeof(HeatmapControl),
            new PropertyMetadata(true, OnDataChanged));

    public static readonly DependencyProperty ShowValuesProperty =
        DependencyProperty.Register(nameof(ShowValues), typeof(bool), typeof(HeatmapControl),
            new PropertyMetadata(false, OnDataChanged));

    public static readonly DependencyProperty ShowCoPProperty =
        DependencyProperty.Register(nameof(ShowCoP), typeof(bool), typeof(HeatmapControl),
            new PropertyMetadata(true, OnDataChanged));

    public static readonly DependencyProperty ShowLegendProperty =
        DependencyProperty.Register(nameof(ShowLegend), typeof(bool), typeof(HeatmapControl),
            new PropertyMetadata(true, OnDataChanged));

    public static readonly DependencyProperty FrameInfoProperty =
        DependencyProperty.Register(nameof(FrameInfo), typeof(SensorFrame), typeof(HeatmapControl),
            new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty ContourLevelsProperty =
        DependencyProperty.Register(nameof(ContourLevels), typeof(int), typeof(HeatmapControl),
            new PropertyMetadata(10, OnDataChanged));

    public static readonly DependencyProperty ColorScaleMaxProperty =
        DependencyProperty.Register(nameof(ColorScaleMax), typeof(double), typeof(HeatmapControl),
            new PropertyMetadata(SensorConfig.MaxForceGrams, OnDataChanged));

    public static readonly DependencyProperty IsPausedProperty =
        DependencyProperty.Register(nameof(IsPaused), typeof(bool), typeof(HeatmapControl),
            new PropertyMetadata(false, OnPausedChanged));

    public static readonly DependencyProperty InterpolationLevelProperty =
        DependencyProperty.Register(nameof(InterpolationLevel), typeof(int), typeof(HeatmapControl),
            new PropertyMetadata(1, OnDataChanged));

    public double[,]? PressureData
    {
        get => (double[,]?)GetValue(PressureDataProperty);
        set => SetValue(PressureDataProperty, value);
    }

    public HeatmapDisplayMode DisplayMode
    {
        get => (HeatmapDisplayMode)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    public bool ShowGrid
    {
        get => (bool)GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    public bool ShowValues
    {
        get => (bool)GetValue(ShowValuesProperty);
        set => SetValue(ShowValuesProperty, value);
    }

    public bool ShowCoP
    {
        get => (bool)GetValue(ShowCoPProperty);
        set => SetValue(ShowCoPProperty, value);
    }

    public bool ShowLegend
    {
        get => (bool)GetValue(ShowLegendProperty);
        set => SetValue(ShowLegendProperty, value);
    }

    public SensorFrame? FrameInfo
    {
        get => (SensorFrame?)GetValue(FrameInfoProperty);
        set => SetValue(FrameInfoProperty, value);
    }

    public int ContourLevels
    {
        get => (int)GetValue(ContourLevelsProperty);
        set => SetValue(ContourLevelsProperty, value);
    }

    /// <summary>
    /// 色階上限 (g/point)：根據設定的總上限換算而來
    /// 例如總上限 500kg → 單點 312.5g → 色階 0~312.5
    /// </summary>
    public double ColorScaleMax
    {
        get => (double)GetValue(ColorScaleMaxProperty);
        set => SetValue(ColorScaleMaxProperty, value);
    }

    public bool IsPaused
    {
        get => (bool)GetValue(IsPausedProperty);
        set => SetValue(IsPausedProperty, value);
    }

    /// <summary>
    /// 內插層級：1=原始(40×40), 3=3×3(120×120), 5=5×5(200×200)
    /// </summary>
    public int InterpolationLevel
    {
        get => (int)GetValue(InterpolationLevelProperty);
        set => SetValue(InterpolationLevelProperty, value);
    }

    private static void OnPausedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeatmapControl ctrl && !(bool)e.NewValue)
        {
            // 恢復即時 → 重設 zoom/pan
            ctrl._zoomScale = 1.0f;
            ctrl._panOffsetX = 0;
            ctrl._panOffsetY = 0;
            ctrl.InvalidateVisual();
        }
    }

    // ── Mouse hover ──
    private int _hoverRow = -1, _hoverCol = -1;
    private float _mapOffsetX, _mapOffsetY, _cellW, _cellH;

    // ── Zoom / Pan (暫停時可用) ──
    private float _zoomScale = 1.0f;
    private float _panOffsetX, _panOffsetY;
    private bool _isPanning;
    private Point _panStart;
    private float _panStartOffsetX, _panStartOffsetY;
    private float _mapSize; // 快取 mapSize 供 zoom 計算

    // ── ROI selection ──
    private bool _isDragging;
    private int _roiStartRow, _roiStartCol;
    private int _roiEndRow = -1, _roiEndCol = -1;

    // Multi-ROI support
    private class HeatmapRoi
    {
        public int R0, C0, R1, C1;
        public SKColor BorderColor;
        public string Label = "";
        public double TotalForceGrams;
    }

    private readonly List<HeatmapRoi> _roiList = new();
    private int _nextRoiColorIndex;

    private static readonly SKColor[] RoiColors = new[]
    {
        new SKColor(255, 0, 200, 180),   // Pink/Magenta
        new SKColor(0, 200, 0, 180),     // Green
        new SKColor(0, 120, 255, 180),   // Blue
        new SKColor(255, 165, 0, 180),   // Orange
        new SKColor(0, 200, 200, 180),   // Cyan
        new SKColor(255, 255, 0, 180),   // Yellow
        new SKColor(180, 0, 255, 180),   // Purple
        new SKColor(255, 100, 100, 180), // Salmon
    };

    // ── 內插渲染快取 ──
    // 40×40 原始解析度 bitmap（快取，避免每幀重建）
    private SKBitmap? _heatBitmap40;

    // 事件
    public event EventHandler<(int row, int col, double pressure)>? PointHovered;

    public HeatmapControl()
    {
        PaintSurface += OnPaintSurface;
        MouseMove += OnMouseMoveHandler;
        MouseLeave += OnMouseLeaveHandler;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseWheel += OnMouseWheel;
        MouseRightButtonDown += OnMouseRightButtonDown;
        MouseRightButtonUp += OnMouseRightButtonUp;
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeatmapControl control)
            control.InvalidateVisual();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        canvas.Clear(SKColors.White); // *** 白底 ***

        var data = PressureData;
        if (data == null)
        {
            DrawPlaceholder(canvas, info);
            return;
        }

        // 計算佈局：左邊=主熱力圖，中間=色階+統計面板，右邊=ROI 放大
        float legendWidth = ShowLegend ? 230 : 0;  // 80(色階) + 150(統計面板)
        float roiZoomWidth = _roiList.Count > 0 ? 220 : 0;
        float padding = 10;
        float headerHeight = 28;
        float availableW = info.Width - legendWidth - roiZoomWidth - padding * 2 - 10;
        float availableH = info.Height - padding * 2 - headerHeight;

        float mapSize = Math.Min(availableW, availableH);
        _mapOffsetX = padding;
        _mapOffsetY = padding + headerHeight;
        _cellW = mapSize / SensorConfig.Cols;
        _cellH = mapSize / SensorConfig.Rows;
        _mapSize = mapSize;

        // ── 暫停時的 zoom/pan 變換（限制在熱力圖區域內）──
        bool isZoomed = IsPaused && (_zoomScale != 1.0f || _panOffsetX != 0 || _panOffsetY != 0);
        if (isZoomed)
        {
            canvas.Save();
            // 裁切區域：熱力圖不超出原始邊界，不會蓋到色階圖/ROI
            canvas.ClipRect(new SKRect(_mapOffsetX, _mapOffsetY, _mapOffsetX + mapSize, _mapOffsetY + mapSize));
            // panOffset 由 zoom-to-cursor 演算法計算，已包含縮放中心資訊
            // 變換順序：先平移再縮放（以原點為基準）
            canvas.Translate(_panOffsetX, _panOffsetY);
            canvas.Scale(_zoomScale, _zoomScale);
        }

        // 繪製熱力圖主體（256 階色階）
        DrawHeatmap(canvas, data);

        // 等高線模式
        if (DisplayMode == HeatmapDisplayMode.Contour)
            DrawContourLines(canvas, data);

        // 繪製格線（由 Grid 按鈕控制 on/off）
        if (ShowGrid) DrawGrid(canvas);

        // 繪製壓力重心
        if (ShowCoP && FrameInfo != null) DrawCenterOfPressure(canvas);

        // 繪製數值（暫停時強制顯示，方便檢視）
        if (ShowValues || IsPaused) DrawValues(canvas, data);

        // 繪製 hover 十字線
        if (_hoverRow >= 0 && _hoverCol >= 0) DrawHoverInfo(canvas, data);

        // 繪製所有 ROI 區域（含力量標籤）
        DrawAllRois(canvas, data);

        // 繪製拖曳中的 ROI 框
        DrawDragRect(canvas);

        // 恢復 zoom/pan 變換
        if (isZoomed)
            canvas.Restore();

        // 繪製顏色圖例
        if (ShowLegend) DrawLegendBar(canvas, info, mapSize);

        // 繪製即時統計面板（色階右側）
        if (ShowLegend) DrawStatsPanel(canvas, info, mapSize);

        // 繪製 ROI 摘要面板
        if (_roiList.Count > 0 && roiZoomWidth > 0)
            DrawRoiSummaryPanel(canvas, data, info, mapSize, legendWidth);

        // 繪製標題列
        DrawHeader(canvas, info);
    }

    private void DrawPlaceholder(SKCanvas canvas, SKImageInfo info)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(180, 180, 180),
            TextSize = 16,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = CjkTypeface
        };
        canvas.DrawText(LocalizationService.T("Heatmap.WaitingData"), info.Width / 2f, info.Height / 2f, paint);
    }

    /// <summary>
    /// 256 階色階熱力圖繪製（支援內插上採樣）
    ///
    /// 核心原理：
    /// - 永遠只建立 40×40 的 SKBitmap（1600 像素），用色階填色
    /// - Level 1：用 FilterQuality.None → SkiaSharp 做最近鄰放大（方塊狀）
    /// - Level 3/5：用 FilterQuality.High → SkiaSharp 做雙線性/雙三次放大（平滑）
    ///
    /// 這樣做的優勢：
    /// - 不需要自行計算 BilinearUpsample（省掉 40000 次浮點運算/幀）
    /// - 不需要分配 120×120 或 200×200 的中間陣列（省掉 GC 壓力）
    /// - SkiaSharp 的 DrawBitmap 放大是 GPU 加速的，極快
    /// - 每幀只處理 1600 像素，無論什麼內插層級
    /// </summary>
    private void DrawHeatmap(SKCanvas canvas, double[,] data)
    {
        int level = InterpolationLevel;
        if (level != 1 && level != 3 && level != 5) level = 1;

        int rows = SensorConfig.Rows;
        int cols = SensorConfig.Cols;

        // 快取 40×40 bitmap，避免每幀重建
        if (_heatBitmap40 == null || _heatBitmap40.Width != cols || _heatBitmap40.Height != rows)
        {
            _heatBitmap40?.Dispose();
            _heatBitmap40 = new SKBitmap(cols, rows, SKColorType.Rgba8888, SKAlphaType.Premul);
        }

        // 填入色階像素（40×40 = 1600 點，極快）
        var pixelPtr = _heatBitmap40.GetPixels();
        unsafe
        {
            var pixels = (uint*)pixelPtr.ToPointer();
            double scaleMax = ColorScaleMax;
            for (int r = 0; r < rows; r++)
            {
                int rowOffset = r * cols;
                for (int c = 0; c < cols; c++)
                {
                    var color = PressureToColor256(data[r, c], scaleMax);
                    pixels[rowOffset + c] = (uint)(color.Alpha << 24 | color.Blue << 16 | color.Green << 8 | color.Red);
                }
            }
        }

        // 根據內插層級選擇放大品質
        // None = 最近鄰（方塊狀，1×1 原始效果）
        // High = 雙三次插值（平滑，3×3/5×5 效果）
        var filterQuality = level == 1 ? SKFilterQuality.None : SKFilterQuality.High;

        using var paint = new SKPaint
        {
            FilterQuality = filterQuality,
            IsAntialias = false
        };

        var destRect = new SKRect(
            _mapOffsetX,
            _mapOffsetY,
            _mapOffsetX + _cellW * cols,
            _mapOffsetY + _cellH * rows);

        canvas.DrawBitmap(_heatBitmap40, destRect, paint);
    }

    /// <summary>
    /// 256 階 Jet colormap（白底版：0 壓力 = 白色）
    /// maxGrams: 色階上限，預設 SensorConfig.MaxForceGrams
    /// </summary>
    public static SKColor PressureToColor256(double pressureGrams, double maxGrams = 0)
    {
        if (pressureGrams <= 0)
            return SKColors.White; // 無壓力 = 白色背景

        if (maxGrams <= 0) maxGrams = SensorConfig.MaxForceGrams;
        double t = Math.Clamp(pressureGrams / maxGrams, 0, 1);

        // 256 階 Jet colormap
        int idx = (int)(t * 255);
        idx = Math.Clamp(idx, 0, 255);

        double r, g, b;
        double n = idx / 255.0;
        if (n < 0.125)
        {
            r = 0; g = 0; b = 0.5 + n * 4;
        }
        else if (n < 0.375)
        {
            r = 0; g = (n - 0.125) * 4; b = 1;
        }
        else if (n < 0.625)
        {
            r = (n - 0.375) * 4; g = 1; b = 1 - (n - 0.375) * 4;
        }
        else if (n < 0.875)
        {
            r = 1; g = 1 - (n - 0.625) * 4; b = 0;
        }
        else
        {
            r = 1 - (n - 0.875) * 2; g = 0; b = 0;
        }

        return new SKColor(
            (byte)(Math.Clamp(r, 0, 1) * 255),
            (byte)(Math.Clamp(g, 0, 1) * 255),
            (byte)(Math.Clamp(b, 0, 1) * 255));
    }

    private void DrawContourLines(SKCanvas canvas, double[,] data)
    {
        int levels = ContourLevels;
        double scaleMax = ColorScaleMax > 0 ? ColorScaleMax : SensorConfig.MaxForceGrams;
        double step = scaleMax / levels;

        using var linePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f,
            IsAntialias = true,
            Color = new SKColor(0, 0, 0, 100) // 黑色半透明等高線
        };

        for (int level = 1; level <= levels; level++)
        {
            double threshold = level * step;
            for (int r = 0; r < SensorConfig.Rows - 1; r++)
            {
                for (int c = 0; c < SensorConfig.Cols - 1; c++)
                {
                    bool tl = data[r, c] >= threshold;
                    bool tr = data[r, c + 1] >= threshold;
                    bool bl = data[r + 1, c] >= threshold;
                    bool br = data[r + 1, c + 1] >= threshold;

                    int config = (tl ? 8 : 0) | (tr ? 4 : 0) | (br ? 2 : 0) | (bl ? 1 : 0);
                    if (config == 0 || config == 15) continue;

                    float x0 = _mapOffsetX + c * _cellW;
                    float y0 = _mapOffsetY + r * _cellH;
                    float x1 = x0 + _cellW;
                    float y1 = y0 + _cellH;
                    float mx = (x0 + x1) / 2;
                    float my = (y0 + y1) / 2;
                    DrawContourSegment(canvas, linePaint, config, x0, y0, x1, y1, mx, my);
                }
            }
        }
    }

    private void DrawContourSegment(SKCanvas canvas, SKPaint paint, int config,
        float x0, float y0, float x1, float y1, float mx, float my)
    {
        float lmx = x0, lmy = (y0 + y1) / 2;
        float rmx = x1, rmy = (y0 + y1) / 2;
        float tmxTop = (x0 + x1) / 2, tmyTop = y0;
        float tmxBot = (x0 + x1) / 2, tmyBot = y1;

        switch (config)
        {
            case 1: case 14: canvas.DrawLine(lmx, lmy, tmxBot, tmyBot, paint); break;
            case 2: case 13: canvas.DrawLine(tmxBot, tmyBot, rmx, rmy, paint); break;
            case 3: case 12: canvas.DrawLine(lmx, lmy, rmx, rmy, paint); break;
            case 4: case 11: canvas.DrawLine(tmxTop, tmyTop, rmx, rmy, paint); break;
            case 6: case 9: canvas.DrawLine(tmxTop, tmyTop, tmxBot, tmyBot, paint); break;
            case 7: case 8: canvas.DrawLine(lmx, lmy, tmxTop, tmyTop, paint); break;
            case 5: case 10:
                canvas.DrawLine(lmx, lmy, tmxTop, tmyTop, paint);
                canvas.DrawLine(tmxBot, tmyBot, rmx, rmy, paint);
                break;
        }
    }

    private void DrawGrid(SKCanvas canvas)
    {
        float totalW = SensorConfig.Cols * _cellW;
        float totalH = SensorConfig.Rows * _cellH;

        using var gridPaint = new SKPaint
        {
            Color = new SKColor(180, 180, 180), // 淺灰格線（白底上清楚可見）
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            IsAntialias = false // 像素對齊，避免高 DPI 模糊
        };

        // 40×40 = 41 條水平線 + 41 條垂直線
        for (int r = 0; r <= SensorConfig.Rows; r++)
        {
            float y = _mapOffsetY + r * _cellH;
            canvas.DrawLine(_mapOffsetX, y, _mapOffsetX + totalW, y, gridPaint);
        }
        for (int c = 0; c <= SensorConfig.Cols; c++)
        {
            float x = _mapOffsetX + c * _cellW;
            canvas.DrawLine(x, _mapOffsetY, x, _mapOffsetY + totalH, gridPaint);
        }

        // 每 10 格加粗（方便辨識 10×10 區塊）
        using var majorPaint = new SKPaint
        {
            Color = new SKColor(120, 120, 120),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            IsAntialias = false
        };
        for (int r = 0; r <= SensorConfig.Rows; r += 10)
        {
            float y = _mapOffsetY + r * _cellH;
            canvas.DrawLine(_mapOffsetX, y, _mapOffsetX + totalW, y, majorPaint);
        }
        for (int c = 0; c <= SensorConfig.Cols; c += 10)
        {
            float x = _mapOffsetX + c * _cellW;
            canvas.DrawLine(x, _mapOffsetY, x, _mapOffsetY + totalH, majorPaint);
        }

        // 外框
        using var borderPaint = new SKPaint
        {
            Color = new SKColor(60, 60, 60),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = false
        };
        canvas.DrawRect(_mapOffsetX, _mapOffsetY, totalW, totalH, borderPaint);
    }

    private void DrawCenterOfPressure(SKCanvas canvas)
    {
        var frame = FrameInfo;
        if (frame == null || frame.TotalForceGrams < SensorConfig.TriggerForceGrams) return;

        float cx = _mapOffsetX + (float)(frame.CenterOfPressureX / SensorConfig.SensorPitchMm) * _cellW;
        float cy = _mapOffsetY + (float)(frame.CenterOfPressureY / SensorConfig.SensorPitchMm) * _cellH;

        using var copPaint = new SKPaint
        {
            Color = new SKColor(220, 20, 20), // 紅色，更明顯
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
            IsAntialias = true
        };
        canvas.DrawCircle(cx, cy, 8, copPaint);
        canvas.DrawLine(cx - 12, cy, cx + 12, cy, copPaint);
        canvas.DrawLine(cx, cy - 12, cx, cy + 12, copPaint);

        using var textPaint = new SKPaint
        {
            Color = new SKColor(220, 20, 20),
            TextSize = 11,
            IsAntialias = true,
            Typeface = CjkBoldTypeface
        };
        canvas.DrawText("CoP", cx + 14, cy + 4, textPaint);
    }

    private void DrawValues(SKCanvas canvas, double[,] data)
    {
        // 根據實際渲染後的格子大小（含 zoom）計算字體
        float effectiveCellW = _cellW * _zoomScale;
        float effectiveCellH = _cellH * _zoomScale;

        // 格子太小則不顯示（zoom 後等效低於 8px）
        if (effectiveCellW < 8 || effectiveCellH < 6) return;

        // 字體大小：隨格子縮放，但有上下限
        float fontSize = Math.Clamp(_cellW * 0.5f, 5, 14);

        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = fontSize,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center
        };

        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                double v = data[r, c];
                if (v <= 0) continue;

                float x = _mapOffsetX + c * _cellW + _cellW / 2;
                float y = _mapOffsetY + r * _cellH + _cellH / 2 + textPaint.TextSize / 3;

                // 根據背景色調整文字色
                var bgColor = PressureToColor256(v, ColorScaleMax);
                double luminance = (0.299 * bgColor.Red + 0.587 * bgColor.Green + 0.114 * bgColor.Blue) / 255;
                textPaint.Color = luminance > 0.5 ? SKColors.Black : SKColors.White;

                canvas.DrawText(v.ToString("F0"), x, y, textPaint);
            }
        }
    }

    private void DrawHoverInfo(SKCanvas canvas, double[,] data)
    {
        if (_hoverRow < 0 || _hoverCol < 0) return;

        float x = _mapOffsetX + _hoverCol * _cellW;
        float y = _mapOffsetY + _hoverRow * _cellH;

        // 十字線
        using var crossPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 100),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0)
        };
        canvas.DrawLine(x + _cellW / 2, _mapOffsetY, x + _cellW / 2,
                       _mapOffsetY + SensorConfig.Rows * _cellH, crossPaint);
        canvas.DrawLine(_mapOffsetX, y + _cellH / 2,
                       _mapOffsetX + SensorConfig.Cols * _cellW, y + _cellH / 2, crossPaint);

        // Highlight cell
        using var hlPaint = new SKPaint
        {
            Color = new SKColor(255, 0, 0, 200),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2
        };
        canvas.DrawRect(x, y, _cellW, _cellH, hlPaint);

        // Info box
        double pressure = data[_hoverRow, _hoverCol];
        string info = $"({_hoverRow + 1},{_hoverCol + 1}) {pressure:F1}g";

        using var bgPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 210),
            Style = SKPaintStyle.Fill
        };
        using var infoPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 13,
            IsAntialias = true
        };

        float boxX = x + _cellW + 5;
        float boxY = y - 10;
        float textW = infoPaint.MeasureText(info);
        canvas.DrawRoundRect(boxX - 3, boxY - 14, textW + 10, 20, 4, 4, bgPaint);
        canvas.DrawText(info, boxX + 2, boxY, infoPaint);
    }

    // ── ROI drawing ──

    /// <summary>
    /// Draw the in-progress drag rectangle (while user is dragging)
    /// </summary>
    private void DrawDragRect(SKCanvas canvas)
    {
        if (!_isDragging) return;

        int r0 = Math.Min(_roiStartRow, _roiEndRow);
        int c0 = Math.Min(_roiStartCol, _roiEndCol);
        int r1 = Math.Max(_roiStartRow, _roiEndRow);
        int c1 = Math.Max(_roiStartCol, _roiEndCol);

        float x = _mapOffsetX + c0 * _cellW;
        float y = _mapOffsetY + r0 * _cellH;
        float w = (c1 - c0 + 1) * _cellW;
        float h = (r1 - r0 + 1) * _cellH;

        using var fillPaint = new SKPaint
        {
            Color = new SKColor(0, 120, 255, 30),
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(x, y, w, h, fillPaint);

        using var borderPaint = new SKPaint
        {
            Color = new SKColor(0, 100, 220, 200),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            PathEffect = SKPathEffect.CreateDash(new float[] { 6, 3 }, 0)
        };
        canvas.DrawRect(x, y, w, h, borderPaint);

        using var labelPaint = new SKPaint
        {
            Color = new SKColor(0, 80, 180),
            TextSize = 11,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas")
        };
        canvas.DrawText($"ROI ({r0 + 1},{c0 + 1})-({r1 + 1},{c1 + 1})", x + 2, y - 3, labelPaint);
    }

    /// <summary>
    /// Draw all confirmed ROI regions with color-coded borders and force labels
    /// </summary>
    private void DrawAllRois(SKCanvas canvas, double[,] data)
    {
        var frame = FrameInfo;
        for (int i = 0; i < _roiList.Count; i++)
        {
            var roi = _roiList[i];

            // Compute total force for this ROI
            double totalGrams = 0;
            for (int r = roi.R0; r <= roi.R1; r++)
                for (int c = roi.C0; c <= roi.C1; c++)
                    if (data[r, c] > 0) totalGrams += data[r, c];
            roi.TotalForceGrams = totalGrams;
            roi.Label = $"{totalGrams / 1000:F1}kg";

            float x = _mapOffsetX + roi.C0 * _cellW;
            float y = _mapOffsetY + roi.R0 * _cellH;
            float w = (roi.C1 - roi.C0 + 1) * _cellW;
            float h = (roi.R1 - roi.R0 + 1) * _cellH;

            // Semi-transparent fill
            using var fillPaint = new SKPaint
            {
                Color = new SKColor(roi.BorderColor.Red, roi.BorderColor.Green, roi.BorderColor.Blue, 25),
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRect(x, y, w, h, fillPaint);

            // Dashed border
            using var borderPaint = new SKPaint
            {
                Color = roi.BorderColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2.5f,
                PathEffect = SKPathEffect.CreateDash(new float[] { 6, 3 }, 0),
                IsAntialias = true
            };
            canvas.DrawRect(x, y, w, h, borderPaint);

            // ROI number label at top-left
            using var indexPaint = new SKPaint
            {
                Color = roi.BorderColor,
                TextSize = 11,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyleWeight.Bold,
                    SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            };
            canvas.DrawText($"ROI {i + 1}", x + 2, y - 3, indexPaint);

            // Force label centered in the ROI box
            float fontSize = Math.Clamp(Math.Min(w, h) * 0.25f, 14, 28);
            using var forcePaint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = fontSize,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center,
                Typeface = CjkBoldTypeface
            };
            float labelTextW = forcePaint.MeasureText(roi.Label);
            float cx = x + w / 2;
            float cy = y + h / 2;

            // Dark semi-transparent background for the label
            float bgPadX = 6, bgPadY = 4;
            float totalLabelH = fontSize + 12; // main + frame line
            using var bgPaint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 160),
                Style = SKPaintStyle.Fill
            };
            float bgW = Math.Max(labelTextW, 60) + bgPadX * 2;
            canvas.DrawRoundRect(cx - bgW / 2, cy - fontSize / 2 - bgPadY,
                bgW, totalLabelH + bgPadY * 2, 4, 4, bgPaint);

            // Main force text
            canvas.DrawText(roi.Label, cx, cy + fontSize * 0.35f, forcePaint);

            // Frame info text below force label
            if (frame != null)
            {
                using var framePaint = new SKPaint
                {
                    Color = new SKColor(200, 200, 200),
                    TextSize = Math.Max(fontSize * 0.5f, 10),
                    IsAntialias = true,
                    TextAlign = SKTextAlign.Center,
                    Typeface = SKTypeface.FromFamilyName("Consolas")
                };
                canvas.DrawText($"F:{frame.FrameIndex}", cx, cy + fontSize * 0.35f + framePaint.TextSize + 2, framePaint);
            }
        }
    }

    /// <summary>
    /// ROI summary panel on the right side, showing stats for all ROIs
    /// </summary>
    private void DrawRoiSummaryPanel(SKCanvas canvas, double[,] data, SKImageInfo info,
        float mapSize, float legendWidth)
    {
        if (_roiList.Count == 0) return;

        float panelX = _mapOffsetX + mapSize + legendWidth + 20;
        float panelY = _mapOffsetY;
        float panelW = 200;

        // Panel title
        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 13,
            IsAntialias = true,
            Typeface = CjkBoldTypeface
        };
        canvas.DrawText($"{LocalizationService.T("Heatmap.ROISummary")} ({_roiList.Count})", panelX, panelY - 6, titlePaint);

        float itemY = panelY + 4;
        float itemHeight = 72;

        for (int i = 0; i < _roiList.Count; i++)
        {
            var roi = _roiList[i];
            if (itemY + itemHeight > info.Height - 10) break; // stop if out of space

            int roiRows = roi.R1 - roi.R0 + 1;
            int roiCols = roi.C1 - roi.C0 + 1;

            // Compute stats
            double peak = 0, sum = 0;
            int active = 0;
            for (int r = roi.R0; r <= roi.R1; r++)
                for (int c = roi.C0; c <= roi.C1; c++)
                {
                    double v = data[r, c];
                    if (v > peak) peak = v;
                    if (v > 0) { sum += v; active++; }
                }

            // Background for this ROI entry
            using var entryBg = new SKPaint
            {
                Color = new SKColor(roi.BorderColor.Red, roi.BorderColor.Green, roi.BorderColor.Blue, 20),
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRoundRect(panelX, itemY, panelW, itemHeight - 4, 4, 4, entryBg);

            // Left color swatch
            using var swatchPaint = new SKPaint
            {
                Color = roi.BorderColor,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRoundRect(panelX + 4, itemY + 4, 8, itemHeight - 12, 2, 2, swatchPaint);

            // ROI info text
            float textX = panelX + 18;
            using var headerPaint = new SKPaint
            {
                Color = SKColors.Black,
                TextSize = 11,
                IsAntialias = true,
                Typeface = CjkBoldTypeface
            };
            canvas.DrawText($"ROI {i + 1}  {sum / 1000:F1}kg", textX, itemY + 14, headerPaint);

            using var detailPaint = new SKPaint
            {
                Color = new SKColor(60, 60, 60),
                TextSize = 10,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Consolas")
            };
            canvas.DrawText($"Peak: {peak:F0}g", textX, itemY + 28, detailPaint);
            canvas.DrawText($"Active: {active}/{roiRows * roiCols} pts", textX, itemY + 40, detailPaint);
            canvas.DrawText($"Size: {roiRows}\u00D7{roiCols}", textX, itemY + 52, detailPaint);

            itemY += itemHeight;
        }
    }

    /// <summary>
    /// 繪製右側顏色圖例條 (Color Legend Bar)
    /// </summary>
    private void DrawLegendBar(SKCanvas canvas, SKImageInfo info, float mapSize)
    {
        float legendX = _mapOffsetX + mapSize + 12;
        float legendY = _mapOffsetY + 5;
        float barWidth = 20;
        float barHeight = mapSize - 10;
        int steps = 256;
        float stepH = barHeight / steps;

        // 動態色階上限
        double scaleMax = ColorScaleMax > 0 ? ColorScaleMax : SensorConfig.MaxForceGrams;

        // 漸層色條
        for (int i = 0; i < steps; i++)
        {
            double pressure = scaleMax * (1.0 - (double)i / steps);
            var color = PressureToColor256(pressure, scaleMax);
            using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
            canvas.DrawRect(legendX, legendY + i * stepH, barWidth, stepH + 1, paint);
        }

        // 外框
        using var borderPaint = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1
        };
        canvas.DrawRect(legendX, legendY, barWidth, barHeight, borderPaint);

        // 刻度和標籤 — 自動產生合適的刻度間距
        using var labelPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 10,
            IsAntialias = true
        };

        // 自動計算刻度：大約 8~12 個刻度
        double tickStep = CalculateNiceTickStep(scaleMax, 10);
        for (double tick = 0; tick <= scaleMax + 0.01; tick += tickStep)
        {
            float ty = legendY + (float)((scaleMax - tick) / scaleMax * barHeight);
            canvas.DrawLine(legendX + barWidth, ty, legendX + barWidth + 4, ty, borderPaint);
            canvas.DrawText($"{tick:F0}", legendX + barWidth + 6, ty + 3, labelPaint);
        }

        // 單位標題
        using var unitPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 10,
            IsAntialias = true,
            Typeface = CjkBoldTypeface
        };
        canvas.DrawText(LocalizationService.T("Heatmap.PressureUnit"), legendX, legendY - 6, unitPaint);
    }

    /// <summary>
    /// 即時統計面板：繪製在色階條右側，顯示關鍵測量數據
    /// 參考 I-Scan 的即時統計佈局，大字體清晰易讀
    /// </summary>
    private void DrawStatsPanel(SKCanvas canvas, SKImageInfo info, float mapSize)
    {
        var frame = FrameInfo;
        if (frame == null) return;

        // 面板位置：緊貼在色階條右側（色階條寬 20px + 刻度約 50px）
        float statsX = _mapOffsetX + mapSize + 80;
        float statsY = _mapOffsetY + 4;

        // ── 面板背景 ──
        float panelW = 140;
        float panelH = Math.Min(mapSize - 8, 460);
        using var bgPaint = new SKPaint
        {
            Color = new SKColor(245, 245, 250),
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRoundRect(statsX - 6, statsY - 6, panelW, panelH, 6, 6, bgPaint);

        using var borderPaint = new SKPaint
        {
            Color = new SKColor(180, 180, 200),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1
        };
        canvas.DrawRoundRect(statsX - 6, statsY - 6, panelW, panelH, 6, 6, borderPaint);

        // ── 標題 ──
        using var titlePaint = new SKPaint
        {
            Color = new SKColor(43, 87, 151),
            TextSize = 14,
            IsAntialias = true,
            Typeface = CjkBoldTypeface
        };
        canvas.DrawText(LocalizationService.T("Heatmap.RealTimeStats"), statsX, statsY + 14, titlePaint);

        // 標題底線
        using var linePaint = new SKPaint
        {
            Color = new SKColor(43, 87, 151, 80),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1
        };
        canvas.DrawLine(statsX, statsY + 20, statsX + panelW - 16, statsY + 20, linePaint);

        // ── 統計數據列 ──
        using var labelPaint = new SKPaint
        {
            Color = new SKColor(90, 90, 100),
            TextSize = 12,
            IsAntialias = true,
            Typeface = CjkTypeface
        };
        using var valuePaint = new SKPaint
        {
            Color = new SKColor(20, 20, 20),
            TextSize = 20,
            IsAntialias = true,
            Typeface = CjkBoldTypeface
        };
        using var unitPaint = new SKPaint
        {
            Color = new SKColor(110, 110, 120),
            TextSize = 13,
            IsAntialias = true,
            Typeface = CjkTypeface
        };

        float y = statsY + 40;
        float lineSpacing = 54;

        // ── Peak ──
        canvas.DrawText(LocalizationService.T("Heatmap.Peak"), statsX, y, labelPaint);
        y += 22;
        canvas.DrawText($"{frame.PeakPressureGrams:F0}", statsX, y, valuePaint);
        canvas.DrawText(" gf", statsX + valuePaint.MeasureText($"{frame.PeakPressureGrams:F0}") + 2, y, unitPaint);
        y += lineSpacing - 22;

        // ── Total Force (顯示為 kg) ──
        canvas.DrawText(LocalizationService.T("Heatmap.TotalForce"), statsX, y, labelPaint);
        y += 22;
        double totalKg = frame.TotalForceGrams / 1000.0;
        string totalStr = totalKg >= 1 ? $"{totalKg:F2}" : $"{frame.TotalForceGrams:F0}";
        string totalUnit = totalKg >= 1 ? " kg" : " gf";
        canvas.DrawText(totalStr, statsX, y, valuePaint);
        canvas.DrawText(totalUnit, statsX + valuePaint.MeasureText(totalStr) + 2, y, unitPaint);
        y += lineSpacing - 22;

        // ── Contact Area ──
        canvas.DrawText(LocalizationService.T("Heatmap.ForceArea"), statsX, y, labelPaint);
        y += 22;
        canvas.DrawText($"{frame.ContactAreaMm2:F1}", statsX, y, valuePaint);
        canvas.DrawText(" mm²", statsX + valuePaint.MeasureText($"{frame.ContactAreaMm2:F1}") + 2, y, unitPaint);
        y += lineSpacing - 22;

        // ── Average ──
        canvas.DrawText(LocalizationService.T("Heatmap.Average"), statsX, y, labelPaint);
        y += 22;
        canvas.DrawText($"{frame.AveragePressureGrams:F0}", statsX, y, valuePaint);
        canvas.DrawText(" gf", statsX + valuePaint.MeasureText($"{frame.AveragePressureGrams:F0}") + 2, y, unitPaint);
        y += lineSpacing - 22;

        // ── Active Points ──
        canvas.DrawText(LocalizationService.T("Heatmap.ActivePoints"), statsX, y, labelPaint);
        y += 22;
        canvas.DrawText($"{frame.ActivePointCount}", statsX, y, valuePaint);
        canvas.DrawText($" / 1600", statsX + valuePaint.MeasureText($"{frame.ActivePointCount}") + 2, y, unitPaint);
        y += lineSpacing - 22;

        // ── Center of Pressure ──
        canvas.DrawText(LocalizationService.T("Heatmap.CoPmm"), statsX, y, labelPaint);
        y += 22;
        using var copPaint = new SKPaint
        {
            Color = new SKColor(220, 20, 20),
            TextSize = 16,
            IsAntialias = true,
            Typeface = CjkBoldTypeface
        };
        if (frame.TotalForceGrams > 0)
            canvas.DrawText($"({frame.CenterOfPressureX:F1}, {frame.CenterOfPressureY:F1})", statsX, y, copPaint);
        else
            canvas.DrawText("—", statsX, y, copPaint);
    }

    /// <summary>
    /// 計算好看的刻度間距（例如 10, 20, 25, 50, 100...）
    /// </summary>
    private static double CalculateNiceTickStep(double range, int targetTicks)
    {
        double rough = range / targetTicks;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double normalized = rough / mag;

        double nice;
        if (normalized <= 1.5) nice = 1;
        else if (normalized <= 3) nice = 2;
        else if (normalized <= 7) nice = 5;
        else nice = 10;

        return nice * mag;
    }

    private void DrawHeader(SKCanvas canvas, SKImageInfo info)
    {
        var frame = FrameInfo;
        if (frame == null) return;

        using var headerPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 13,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas")
        };

        string modeText = DisplayMode switch
        {
            HeatmapDisplayMode.Heatmap => "2D Heatmap",
            HeatmapDisplayMode.Contour => "2D Contour",
            _ => ""
        };

        string text = $"{modeText} | Frame #{frame.FrameIndex} | " +
                      $"Peak: {frame.PeakPressureGrams:F0}g @({frame.PeakRow + 1},{frame.PeakCol + 1}) | " +
                      $"Total: {frame.TotalForceGrams:F0}g | " +
                      $"Active: {frame.ActivePointCount} pts | " +
                      $"Area: {frame.ContactAreaMm2:F1}mm\u00B2";
        canvas.DrawText(text, _mapOffsetX + 2, _mapOffsetY - 8, headerPaint);
    }

    // ── Mouse handlers ──

    private (int row, int col) PixelToCell(Point pos)
    {
        var source = PresentationSource.FromVisual(this);
        double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        float px = (float)(pos.X * dpiScale);
        float py = (float)(pos.Y * dpiScale);
        int col = (int)((px - _mapOffsetX) / _cellW);
        int row = (int)((py - _mapOffsetY) / _cellH);
        return (row, col);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Double-click clears all ROIs
        if (e.ClickCount == 2)
        {
            ClearAllRois();
            e.Handled = true;
            return;
        }

        var pos = e.GetPosition(this);
        var (row, col) = PixelToCell(pos);
        if (row >= 0 && row < SensorConfig.Rows && col >= 0 && col < SensorConfig.Cols)
        {
            _isDragging = true;
            _roiStartRow = row;
            _roiStartCol = col;
            _roiEndRow = row;
            _roiEndCol = col;
            CaptureMouse();
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ReleaseMouseCapture();

        int r0 = Math.Min(_roiStartRow, _roiEndRow);
        int c0 = Math.Min(_roiStartCol, _roiEndCol);
        int r1 = Math.Max(_roiStartRow, _roiEndRow);
        int c1 = Math.Max(_roiStartCol, _roiEndCol);

        // At least 2x2 to be a valid ROI
        if (r1 - r0 >= 1 && c1 - c0 >= 1)
        {
            var color = RoiColors[_nextRoiColorIndex % RoiColors.Length];
            _nextRoiColorIndex++;
            _roiList.Add(new HeatmapRoi
            {
                R0 = r0, C0 = c0, R1 = r1, C1 = c1,
                BorderColor = color
            });
        }
        InvalidateVisual();
    }

    /// <summary>
    /// Clear all ROI regions
    /// </summary>
    public void ClearAllRois()
    {
        _roiList.Clear();
        _nextRoiColorIndex = 0;
        InvalidateVisual();
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        // 暫停時右鍵拖曳 → 平移
        if (_isPanning)
        {
            var source = PresentationSource.FromVisual(this);
            double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            _panOffsetX = _panStartOffsetX + (float)((pos.X - _panStart.X) * dpiScale);
            _panOffsetY = _panStartOffsetY + (float)((pos.Y - _panStart.Y) * dpiScale);
            InvalidateVisual();
            return;
        }

        var (row, col) = PixelToCell(pos);

        if (_isDragging)
        {
            _roiEndRow = Math.Clamp(row, 0, SensorConfig.Rows - 1);
            _roiEndCol = Math.Clamp(col, 0, SensorConfig.Cols - 1);
            InvalidateVisual();
            return;
        }

        if (row >= 0 && row < SensorConfig.Rows && col >= 0 && col < SensorConfig.Cols)
        {
            if (row != _hoverRow || col != _hoverCol)
            {
                _hoverRow = row;
                _hoverCol = col;
                InvalidateVisual();

                if (PressureData != null)
                    PointHovered?.Invoke(this, (row, col, PressureData[row, col]));
            }
        }
        else
        {
            if (_hoverRow >= 0)
            {
                _hoverRow = -1;
                _hoverCol = -1;
                InvalidateVisual();
            }
        }
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        _hoverRow = -1;
        _hoverCol = -1;
        InvalidateVisual();
    }

    // ── 暫停時的 Zoom / Pan ──

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!IsPaused) return;

        // 取得滑鼠在 SKSurface 座標系中的位置
        var pos = e.GetPosition(this);
        var source = PresentationSource.FromVisual(this);
        double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        float mouseX = (float)(pos.X * dpiScale);
        float mouseY = (float)(pos.Y * dpiScale);

        float factor = e.Delta > 0 ? 1.2f : 1 / 1.2f;
        float newScale = Math.Clamp(_zoomScale * factor, 1.0f, 10.0f);

        if (Math.Abs(newScale - _zoomScale) < 0.001f)
        {
            e.Handled = true;
            return;
        }

        // ── Zoom-to-cursor 演算法 ──
        // 在縮放前，滑鼠指向的「世界座標」為：
        //   worldX = (mouseX - panX) / oldScale - mapOffsetX) / oldScale  ...
        // 簡化：保持滑鼠下方的內容不動
        // 公式：newPan = mousePos - (mousePos - oldPan) * (newScale / oldScale)
        float ratio = newScale / _zoomScale;
        _panOffsetX = mouseX - (mouseX - _panOffsetX) * ratio;
        _panOffsetY = mouseY - (mouseY - _panOffsetY) * ratio;

        _zoomScale = newScale;

        // 縮小到 1x 時重置
        if (_zoomScale <= 1.01f)
        {
            _zoomScale = 1.0f;
            _panOffsetX = 0;
            _panOffsetY = 0;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsPaused || _zoomScale <= 1.01f) return;

        _isPanning = true;
        _panStart = e.GetPosition(this);
        _panStartOffsetX = _panOffsetX;
        _panStartOffsetY = _panOffsetY;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning) return;

        _isPanning = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }
}

public enum HeatmapDisplayMode
{
    Heatmap,    // 標準 2D 熱力圖
    Contour     // 2D 等高線 + 熱力圖
}
