using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using PressureMappingSystem.Models;
using PressureMappingSystem.Services;

namespace PressureMappingSystem.Controls;

/// <summary>
/// 3D Surface control using pure WPF Viewport3D (no external dependencies).
/// Supports orbit, pan, zoom via mouse, Jet colormap per-vertex coloring.
/// </summary>
public partial class Surface3DControl : UserControl
{
    // ── Dependency Properties ──
    public static readonly DependencyProperty PressureDataProperty =
        DependencyProperty.Register(nameof(PressureData), typeof(double[,]), typeof(Surface3DControl),
            new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty ColorScaleMaxProperty =
        DependencyProperty.Register(nameof(ColorScaleMax), typeof(double), typeof(Surface3DControl),
            new PropertyMetadata(SensorConfig.MaxForceGrams, OnDataChanged));

    public double[,]? PressureData
    {
        get => (double[,]?)GetValue(PressureDataProperty);
        set => SetValue(PressureDataProperty, value);
    }

    public double ColorScaleMax
    {
        get => (double)GetValue(ColorScaleMaxProperty);
        set => SetValue(ColorScaleMaxProperty, value);
    }

    // ── Camera orbit state ──
    private double _cameraTheta = 225.0;   // horizontal angle (degrees)
    private double _cameraPhi = 35.0;      // vertical angle (degrees)
    private double _cameraRadius = 50.0;   // distance from target
    private Point3D _cameraTarget = new(10, 10, 3);
    private Point _lastMousePos;
    private bool _isLeftDragging;
    private bool _isRightDragging;

    // ── Render state ──
    private double _heightScale = 1.0;
    private bool _showWireframe;
    private bool _needsUpdate = true;
    private double[,]? _lastData;
    private DateTime _lastRenderTime = DateTime.MinValue;
    private const int MinRenderIntervalMs = 50;

    private bool _legendBuilt;

    public Surface3DControl()
    {
        InitializeComponent();
        UpdateCameraPosition();
        BuildBaseGrid();
        CompositionTarget.Rendering += OnCompositionRendering;
        Loaded += (_, _) => BuildColorLegend();
        SizeChanged += (_, _) => BuildColorLegend();
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Surface3DControl ctrl)
        {
            if (e.Property == PressureDataProperty)
                ctrl._lastData = e.NewValue as double[,];

            ctrl._needsUpdate = true;

            // 色階範圍變更時重建圖例
            if (e.Property == ColorScaleMaxProperty)
                ctrl.BuildColorLegend();
        }
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        if (!_needsUpdate) return;
        if ((DateTime.Now - _lastRenderTime).TotalMilliseconds < MinRenderIntervalMs) return;

        _needsUpdate = false;
        _lastRenderTime = DateTime.Now;
        UpdateSurface(_lastData);
    }

    // ═══════════════════════════════════════════
    //  Surface Mesh Generation
    // ═══════════════════════════════════════════

    private void UpdateSurface(double[,]? data)
    {
        if (data == null)
        {
            SurfaceModel.Content = null;
            return;
        }

        int rows = SensorConfig.Rows;
        int cols = SensorConfig.Cols;
        double pitch = SensorConfig.SensorPitchMm;

        var mesh = new MeshGeometry3D();
        var positions = new Point3DCollection(rows * cols);
        var normals = new Vector3DCollection(rows * cols);
        var indices = new Int32Collection((rows - 1) * (cols - 1) * 6);

        // Build vertices
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double x = c * pitch;
                double y = r * pitch;
                double pressure = data[r, c];
                double scaleMax = ColorScaleMax > 0 ? ColorScaleMax : SensorConfig.MaxForceGrams;
                double z = (pressure / scaleMax) * 10.0 * _heightScale;
                positions.Add(new Point3D(x, y, z));
            }
        }

        // Build triangle indices
        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < cols - 1; c++)
            {
                int i0 = r * cols + c;
                int i1 = r * cols + c + 1;
                int i2 = (r + 1) * cols + c;
                int i3 = (r + 1) * cols + c + 1;

                indices.Add(i0); indices.Add(i2); indices.Add(i1);
                indices.Add(i1); indices.Add(i2); indices.Add(i3);
            }
        }

        // Compute normals
        var normalArray = new Vector3D[positions.Count];
        for (int i = 0; i < indices.Count; i += 3)
        {
            var p0 = positions[indices[i]];
            var p1 = positions[indices[i + 1]];
            var p2 = positions[indices[i + 2]];
            var v1 = p1 - p0;
            var v2 = p2 - p0;
            var normal = Vector3D.CrossProduct(v1, v2);
            normal.Normalize();
            normalArray[indices[i]] += normal;
            normalArray[indices[i + 1]] += normal;
            normalArray[indices[i + 2]] += normal;
        }
        normals.Clear();
        foreach (var n in normalArray)
        {
            var nrm = n;
            nrm.Normalize();
            normals.Add(nrm);
        }

        mesh.Positions = positions;
        mesh.TriangleIndices = indices;
        mesh.Normals = normals;

        // Build per-vertex colored material via texture
        mesh.TextureCoordinates = BuildTextureCoords(data, rows, cols);
        var material = CreateJetColormapMaterial();

        var group = new Model3DGroup();
        group.Children.Add(new GeometryModel3D
        {
            Geometry = mesh,
            Material = material,
            BackMaterial = material
        });

        // Wireframe overlay (semi-transparent edges)
        if (_showWireframe)
        {
            var wireMat = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)));
            group.Children.Add(new GeometryModel3D
            {
                Geometry = BuildWireframeMesh(positions, rows, cols),
                Material = wireMat,
                BackMaterial = wireMat
            });
        }

        SurfaceModel.Content = group;
    }

    private PointCollection BuildTextureCoords(double[,] data, int rows, int cols)
    {
        double scaleMax = ColorScaleMax > 0 ? ColorScaleMax : SensorConfig.MaxForceGrams;
        var texCoords = new PointCollection(rows * cols);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double norm = Math.Clamp(data[r, c] / scaleMax, 0, 1);
                texCoords.Add(new Point(norm, 0.5));
            }
        }
        return texCoords;
    }

    /// <summary>
    /// Create a 1D texture brush with Jet colormap
    /// </summary>
    private Material CreateJetColormapMaterial()
    {
        int texWidth = 256;
        var pixels = new byte[texWidth * 4];

        for (int i = 0; i < texWidth; i++)
        {
            double t = (double)i / (texWidth - 1);
            JetColor(t, out byte r, out byte g, out byte b);
            pixels[i * 4 + 0] = b;
            pixels[i * 4 + 1] = g;
            pixels[i * 4 + 2] = r;
            pixels[i * 4 + 3] = 255;
        }

        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
            texWidth, 1, 96, 96, PixelFormats.Bgra32, null, pixels, texWidth * 4);

        var brush = new ImageBrush(bmp)
        {
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 1, 1),
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill
        };

        return new DiffuseMaterial(brush);
    }

    /// <summary>
    /// Jet colormap: blue -> cyan -> green -> yellow -> red
    /// </summary>
    private static void JetColor(double t, out byte r, out byte g, out byte b)
    {
        t = Math.Clamp(t, 0, 1);
        double rr, gg, bb;

        if (t < 0.125)
        {
            rr = 0; gg = 0; bb = 0.5 + t / 0.125 * 0.5;
        }
        else if (t < 0.375)
        {
            rr = 0; gg = (t - 0.125) / 0.25; bb = 1;
        }
        else if (t < 0.625)
        {
            rr = (t - 0.375) / 0.25; gg = 1; bb = 1 - (t - 0.375) / 0.25;
        }
        else if (t < 0.875)
        {
            rr = 1; gg = 1 - (t - 0.625) / 0.25; bb = 0;
        }
        else
        {
            rr = 1 - (t - 0.875) / 0.125 * 0.5; gg = 0; bb = 0;
        }

        r = (byte)(rr * 255);
        g = (byte)(gg * 255);
        b = (byte)(bb * 255);
    }

    /// <summary>
    /// Build thin quads along grid edges for wireframe effect
    /// </summary>
    private MeshGeometry3D BuildWireframeMesh(Point3DCollection surfacePositions, int rows, int cols)
    {
        var mesh = new MeshGeometry3D();
        var pos = new Point3DCollection();
        var idx = new Int32Collection();
        double lineWidth = 0.03;

        // Horizontal lines (every 4 rows)
        for (int r = 0; r < rows; r += 4)
        {
            for (int c = 0; c < cols - 1; c++)
            {
                int i0 = r * cols + c;
                int i1 = r * cols + c + 1;
                var p0 = surfacePositions[i0];
                var p1 = surfacePositions[i1];
                AddLineQuad(pos, idx, p0, p1, lineWidth);
            }
        }

        // Vertical lines (every 4 cols)
        for (int c = 0; c < cols; c += 4)
        {
            for (int r = 0; r < rows - 1; r++)
            {
                int i0 = r * cols + c;
                int i1 = (r + 1) * cols + c;
                var p0 = surfacePositions[i0];
                var p1 = surfacePositions[i1];
                AddLineQuad(pos, idx, p0, p1, lineWidth);
            }
        }

        mesh.Positions = pos;
        mesh.TriangleIndices = idx;
        return mesh;
    }

    private static void AddLineQuad(Point3DCollection positions, Int32Collection indices, Point3D p0, Point3D p1, double width)
    {
        var up = new Vector3D(0, 0, width);
        int baseIdx = positions.Count;
        positions.Add(p0 + up);
        positions.Add(p0 - up);
        positions.Add(p1 + up);
        positions.Add(p1 - up);
        indices.Add(baseIdx); indices.Add(baseIdx + 1); indices.Add(baseIdx + 2);
        indices.Add(baseIdx + 1); indices.Add(baseIdx + 3); indices.Add(baseIdx + 2);
    }

    // ═══════════════════════════════════════════
    //  Base Grid
    // ═══════════════════════════════════════════

    private void BuildBaseGrid()
    {
        var mesh = new MeshGeometry3D();
        var pos = new Point3DCollection();
        var idx = new Int32Collection();
        double size = SensorConfig.Rows * SensorConfig.SensorPitchMm;
        double lineW = 0.015;

        // Draw grid lines on z=0 plane
        for (int i = 0; i <= 40; i += 5)
        {
            double v = i * SensorConfig.SensorPitchMm;
            // Horizontal
            AddFlatLineQuad(pos, idx, new Point3D(0, v, 0), new Point3D(size, v, 0), lineW);
            // Vertical
            AddFlatLineQuad(pos, idx, new Point3D(v, 0, 0), new Point3D(v, size, 0), lineW);
        }

        mesh.Positions = pos;
        mesh.TriangleIndices = idx;

        var mat = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)));
        GridModel.Content = new GeometryModel3D { Geometry = mesh, Material = mat, BackMaterial = mat };
    }

    private static void AddFlatLineQuad(Point3DCollection positions, Int32Collection indices, Point3D p0, Point3D p1, double width)
    {
        var dir = p1 - p0;
        dir.Normalize();
        var perp = new Vector3D(-dir.Y, dir.X, 0) * width;

        int baseIdx = positions.Count;
        positions.Add(p0 + perp);
        positions.Add(p0 - perp);
        positions.Add(p1 + perp);
        positions.Add(p1 - perp);
        indices.Add(baseIdx); indices.Add(baseIdx + 1); indices.Add(baseIdx + 2);
        indices.Add(baseIdx + 1); indices.Add(baseIdx + 3); indices.Add(baseIdx + 2);
    }

    // ═══════════════════════════════════════════
    //  Camera Control (Orbit / Pan / Zoom)
    // ═══════════════════════════════════════════

    private void UpdateCameraPosition()
    {
        double thetaRad = _cameraTheta * Math.PI / 180.0;
        double phiRad = _cameraPhi * Math.PI / 180.0;

        double x = _cameraRadius * Math.Cos(phiRad) * Math.Cos(thetaRad);
        double y = _cameraRadius * Math.Cos(phiRad) * Math.Sin(thetaRad);
        double z = _cameraRadius * Math.Sin(phiRad);

        var pos = new Point3D(
            _cameraTarget.X + x,
            _cameraTarget.Y + y,
            _cameraTarget.Z + z);

        var lookDir = _cameraTarget - pos;

        Camera3D.Position = pos;
        Camera3D.LookDirection = lookDir;
        Camera3D.UpDirection = new Vector3D(0, 0, 1);
    }

    // Left drag: orbit
    private void Viewport3D_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isLeftDragging = true;
        _lastMousePos = e.GetPosition(Viewport3D);
        Viewport3D.CaptureMouse();
    }

    private void Viewport3D_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isLeftDragging = false;
        if (!_isRightDragging) Viewport3D.ReleaseMouseCapture();
    }

    // Right drag: pan
    private void Viewport3D_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isRightDragging = true;
        _lastMousePos = e.GetPosition(Viewport3D);
        Viewport3D.CaptureMouse();
    }

    private void Viewport3D_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isRightDragging = false;
        if (!_isLeftDragging) Viewport3D.ReleaseMouseCapture();
    }

    private void Viewport3D_MouseMove(object sender, MouseEventArgs e)
    {
        var currentPos = e.GetPosition(Viewport3D);
        double dx = currentPos.X - _lastMousePos.X;
        double dy = currentPos.Y - _lastMousePos.Y;
        _lastMousePos = currentPos;

        if (_isLeftDragging)
        {
            // Orbit
            _cameraTheta -= dx * 0.5;
            _cameraPhi += dy * 0.5;
            _cameraPhi = Math.Clamp(_cameraPhi, -89, 89);
            UpdateCameraPosition();
        }
        else if (_isRightDragging)
        {
            // Pan
            double thetaRad = _cameraTheta * Math.PI / 180.0;
            double panSpeed = _cameraRadius * 0.002;

            var right = new Vector3D(-Math.Sin(thetaRad), Math.Cos(thetaRad), 0);
            var up = new Vector3D(0, 0, 1);

            _cameraTarget += right * (-dx * panSpeed) + up * (dy * panSpeed);
            UpdateCameraPosition();
        }
    }

    // Scroll: zoom
    private void Viewport3D_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 0.9 : 1.1;
        _cameraRadius *= factor;
        _cameraRadius = Math.Clamp(_cameraRadius, 5, 200);
        UpdateCameraPosition();
    }

    // ── UI Event Handlers ──

    private void HeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _heightScale = e.NewValue;
        _needsUpdate = true;
    }

    private void OnRenderOptionChanged(object sender, RoutedEventArgs e)
    {
        _showWireframe = WireframeCheck?.IsChecked ?? false;
        _needsUpdate = true;
    }

    /// <summary>
    /// 建立 3D 視圖右側的色階圖
    /// </summary>
    private void BuildColorLegend()
    {
        if (LegendCanvas == null) return;

        LegendCanvas.Children.Clear();
        double canvasH = LegendCanvas.ActualHeight;
        if (canvasH < 20) canvasH = 400;
        double barWidth = 22;
        double barX = 4;
        int steps = 256;
        double stepH = canvasH / steps;

        // 漸層色條
        for (int i = 0; i < steps; i++)
        {
            double t = 1.0 - (double)i / steps;
            JetColor(t, out byte r, out byte g, out byte b);
            var rect = new Rectangle
            {
                Width = barWidth,
                Height = stepH + 1,
                Fill = new SolidColorBrush(Color.FromRgb(r, g, b))
            };
            Canvas.SetLeft(rect, barX);
            Canvas.SetTop(rect, i * stepH);
            LegendCanvas.Children.Add(rect);
        }

        // 外框
        var border = new Rectangle
        {
            Width = barWidth,
            Height = canvasH,
            Stroke = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
            StrokeThickness = 1,
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(border, barX);
        Canvas.SetTop(border, 0);
        LegendCanvas.Children.Add(border);

        // 刻度標籤 — 動態範圍
        double maxVal = ColorScaleMax > 0 ? ColorScaleMax : SensorConfig.MaxForceGrams;
        double tickStep = CalculateNiceTickStep(maxVal, 10);
        for (double tick = 0; tick <= maxVal + 0.01; tick += tickStep)
        {
            double y = (maxVal - tick) / maxVal * canvasH;
            var line = new Line
            {
                X1 = barX + barWidth,
                Y1 = y,
                X2 = barX + barWidth + 4,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                StrokeThickness = 1
            };
            LegendCanvas.Children.Add(line);

            var label = new TextBlock
            {
                Text = $"{tick:F0}",
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                FontSize = 9
            };
            Canvas.SetLeft(label, barX + barWidth + 5);
            Canvas.SetTop(label, y - 6);
            LegendCanvas.Children.Add(label);
        }

        LegendRangeText.Text = $"0 - {maxVal:F0} gf";
        _legendBuilt = true;
    }

    private void ResetView_Click(object sender, RoutedEventArgs e)
    {
        _cameraTheta = 225.0;
        _cameraPhi = 35.0;
        _cameraRadius = 50.0;
        _cameraTarget = new Point3D(10, 10, 3);
        UpdateCameraPosition();
    }

    private static double CalculateNiceTickStep(double range, int targetTicks)
    {
        if (range <= 0) return 1;
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
}
