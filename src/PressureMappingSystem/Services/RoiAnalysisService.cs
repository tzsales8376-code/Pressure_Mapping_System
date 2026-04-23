using PressureMappingSystem.Models;

namespace PressureMappingSystem.Services;

/// <summary>
/// ROI (Region of Interest) 區域選取分析服務
/// 支援矩形、橢圓、自由形狀的選取區域
/// 對選取區域內的感測點進行獨立統計分析
/// </summary>
public class RoiAnalysisService
{
    private readonly List<RoiRegion> _regions = new();

    public IReadOnlyList<RoiRegion> Regions => _regions;
    public event EventHandler<RoiRegion>? RegionAdded;
    public event EventHandler? RegionsCleared;

    /// <summary>
    /// 新增矩形 ROI
    /// </summary>
    public RoiRegion AddRectangle(string name, int startRow, int startCol, int endRow, int endCol)
    {
        var region = new RoiRegion
        {
            Name = name,
            Type = RoiType.Rectangle,
            StartRow = Math.Min(startRow, endRow),
            StartCol = Math.Min(startCol, endCol),
            EndRow = Math.Max(startRow, endRow),
            EndCol = Math.Max(startCol, endCol),
            Color = GetNextColor()
        };

        // 建立 mask
        region.BuildRectangleMask();
        _regions.Add(region);
        RegionAdded?.Invoke(this, region);
        return region;
    }

    /// <summary>
    /// 新增橢圓 ROI
    /// </summary>
    public RoiRegion AddEllipse(string name, int centerRow, int centerCol, int radiusRow, int radiusCol)
    {
        var region = new RoiRegion
        {
            Name = name,
            Type = RoiType.Ellipse,
            CenterRow = centerRow,
            CenterCol = centerCol,
            RadiusRow = radiusRow,
            RadiusCol = radiusCol,
            Color = GetNextColor()
        };

        region.BuildEllipseMask();
        _regions.Add(region);
        RegionAdded?.Invoke(this, region);
        return region;
    }

    /// <summary>
    /// 新增自由形狀 ROI (由一組感測點座標定義)
    /// </summary>
    public RoiRegion AddFreeForm(string name, List<(int row, int col)> points)
    {
        var region = new RoiRegion
        {
            Name = name,
            Type = RoiType.FreeForm,
            FreeFormPoints = points,
            Color = GetNextColor()
        };

        region.BuildFreeFormMask();
        _regions.Add(region);
        RegionAdded?.Invoke(this, region);
        return region;
    }

    /// <summary>
    /// 移除指定 ROI
    /// </summary>
    public void RemoveRegion(RoiRegion region) => _regions.Remove(region);

    /// <summary>
    /// 清除所有 ROI
    /// </summary>
    public void ClearAll()
    {
        _regions.Clear();
        RegionsCleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 分析指定幀在某個 ROI 區域內的統計資料
    /// </summary>
    public RoiStatistics AnalyzeRegion(RoiRegion region, SensorFrame frame)
    {
        var stats = new RoiStatistics { RegionName = region.Name };

        double totalForce = 0;
        double peakPressure = 0;
        int peakRow = 0, peakCol = 0;
        int activeCount = 0;
        int totalPoints = 0;
        double weightedX = 0, weightedY = 0;
        double minPressure = double.MaxValue;
        double sumSquared = 0;

        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                if (!region.Mask[r, c]) continue;

                totalPoints++;
                double p = frame.PressureGrams[r, c];

                if (p >= SensorConfig.TriggerForceGrams)
                {
                    activeCount++;
                    totalForce += p;
                    sumSquared += p * p;
                    weightedX += c * SensorConfig.SensorPitchMm * p;
                    weightedY += r * SensorConfig.SensorPitchMm * p;

                    if (p > peakPressure) { peakPressure = p; peakRow = r; peakCol = c; }
                    if (p < minPressure) minPressure = p;
                }
            }
        }

        stats.TotalPoints = totalPoints;
        stats.ActivePoints = activeCount;
        stats.PeakPressureGrams = peakPressure;
        stats.PeakRow = peakRow;
        stats.PeakCol = peakCol;
        stats.TotalForceGrams = totalForce;
        stats.ContactAreaMm2 = activeCount * SensorConfig.SensingAreaMm * SensorConfig.SensingAreaMm;
        stats.AveragePressureGrams = activeCount > 0 ? totalForce / activeCount : 0;
        stats.MinPressureGrams = activeCount > 0 ? minPressure : 0;

        // Standard Deviation
        if (activeCount > 1)
        {
            double mean = stats.AveragePressureGrams;
            stats.StdDevPressureGrams = Math.Sqrt((sumSquared / activeCount) - (mean * mean));
        }

        // Center of Pressure within ROI
        if (totalForce > 0)
        {
            stats.CenterOfPressureX = weightedX / totalForce;
            stats.CenterOfPressureY = weightedY / totalForce;
        }

        // 覆蓋率
        stats.CoveragePercent = totalPoints > 0 ? (double)activeCount / totalPoints * 100 : 0;

        return stats;
    }

    /// <summary>
    /// 分析所有 ROI 區域
    /// </summary>
    public List<RoiStatistics> AnalyzeAllRegions(SensorFrame frame)
    {
        return _regions.Select(r => AnalyzeRegion(r, frame)).ToList();
    }

    private int _colorIndex = 0;
    private static readonly string[] RoiColors = {
        "#FF4CAF50", "#FF2196F3", "#FFFF9800", "#FFE91E63",
        "#FF9C27B0", "#FF00BCD4", "#FFCDDC39", "#FF795548"
    };

    private string GetNextColor() => RoiColors[_colorIndex++ % RoiColors.Length];
}

/// <summary>
/// ROI 區域定義
/// </summary>
public class RoiRegion
{
    public string Name { get; set; } = "";
    public RoiType Type { get; set; }
    public string Color { get; set; } = "#FF4CAF50";

    // Rectangle params
    public int StartRow { get; set; }
    public int StartCol { get; set; }
    public int EndRow { get; set; }
    public int EndCol { get; set; }

    // Ellipse params
    public int CenterRow { get; set; }
    public int CenterCol { get; set; }
    public int RadiusRow { get; set; }
    public int RadiusCol { get; set; }

    // FreeForm params
    public List<(int row, int col)> FreeFormPoints { get; set; } = new();

    /// <summary>選取遮罩 (true = 在 ROI 內)</summary>
    public bool[,] Mask { get; set; } = new bool[SensorConfig.Rows, SensorConfig.Cols];

    public void BuildRectangleMask()
    {
        Mask = new bool[SensorConfig.Rows, SensorConfig.Cols];
        for (int r = StartRow; r <= EndRow && r < SensorConfig.Rows; r++)
            for (int c = StartCol; c <= EndCol && c < SensorConfig.Cols; c++)
                Mask[r, c] = true;
    }

    public void BuildEllipseMask()
    {
        Mask = new bool[SensorConfig.Rows, SensorConfig.Cols];
        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                double dr = (double)(r - CenterRow) / Math.Max(1, RadiusRow);
                double dc = (double)(c - CenterCol) / Math.Max(1, RadiusCol);
                if (dr * dr + dc * dc <= 1.0)
                    Mask[r, c] = true;
            }
        }
    }

    public void BuildFreeFormMask()
    {
        Mask = new bool[SensorConfig.Rows, SensorConfig.Cols];
        // Point-in-polygon using ray casting for the convex hull of points
        if (FreeFormPoints.Count < 3) return;

        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                if (IsPointInPolygon(r, c, FreeFormPoints))
                    Mask[r, c] = true;
            }
        }
    }

    private static bool IsPointInPolygon(int testR, int testC, List<(int row, int col)> polygon)
    {
        bool inside = false;
        int n = polygon.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if (((polygon[i].row > testR) != (polygon[j].row > testR)) &&
                (testC < (polygon[j].col - polygon[i].col) * (testR - polygon[i].row)
                         / (polygon[j].row - polygon[i].row) + polygon[i].col))
            {
                inside = !inside;
            }
        }
        return inside;
    }
}

/// <summary>
/// ROI 區域統計結果
/// </summary>
public class RoiStatistics
{
    public string RegionName { get; set; } = "";
    public int TotalPoints { get; set; }
    public int ActivePoints { get; set; }
    public double PeakPressureGrams { get; set; }
    public int PeakRow { get; set; }
    public int PeakCol { get; set; }
    public double TotalForceGrams { get; set; }
    public double AveragePressureGrams { get; set; }
    public double MinPressureGrams { get; set; }
    public double StdDevPressureGrams { get; set; }
    public double ContactAreaMm2 { get; set; }
    public double CenterOfPressureX { get; set; }
    public double CenterOfPressureY { get; set; }
    public double CoveragePercent { get; set; }
}

public enum RoiType
{
    Rectangle,
    Ellipse,
    FreeForm
}
