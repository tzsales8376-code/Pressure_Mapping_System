using System.IO;
using PressureMappingSystem.Models;

namespace PressureMappingSystem.Services;

/// <summary>
/// 校正服務：管理校正配方、執行電阻→壓力轉換
///
/// 校正原理（依據 FS-ARR-40X40-S20 規格書的 R@F 曲線）：
/// - 電阻與壓力呈非線性反比關係
/// - 未施力: R > 1MΩ (開路)
/// - 觸發力 5g: R ≈ 500KΩ
/// - 最大力 1000g: R ≈ 0.2KΩ
/// - 曲線特性：低壓力區電阻變化劇烈，高壓力區趨於平緩
/// </summary>
public class CalibrationService
{
    private readonly Dictionary<string, CalibrationRecipe> _recipes = new();
    private CalibrationRecipe? _activeRecipe;
    private readonly string _calibrationFolder;

    public CalibrationRecipe? ActiveRecipe => _activeRecipe;
    public IReadOnlyDictionary<string, CalibrationRecipe> Recipes => _recipes;

    public event EventHandler<string>? RecipeChanged;

    public CalibrationService(string calibrationFolder)
    {
        _calibrationFolder = calibrationFolder;
        Directory.CreateDirectory(_calibrationFolder);
        InitializeDefaultRecipes();
        LoadSavedRecipes();
    }

    /// <summary>
    /// 初始化出廠預設校正配方
    /// 根據規格書 R@F 曲線特性建立多組校正基準
    /// </summary>
    private void InitializeDefaultRecipes()
    {
        // ── Recipe 1: 通用標準校正 (Universal Standard) ──
        // 依據規格書 R@F 曲線建立的完整校正表
        var universal = new CalibrationRecipe
        {
            RecipeId = "STD-001",
            Name = "通用標準校正",
            Description = "依據 FS-ARR-40X40-S20 規格書 R@F 特性曲線建立的通用校正配方",
            SensorSerialNumber = "UNIVERSAL",
            Interpolation = InterpolationMethod.CubicSpline,
            LookupTable = new List<CalibrationPoint>
            {
                // 電阻(KΩ)  壓力(g)   不確定度(%)
                // 根據規格書 R@F 曲線: 高電阻(低壓) → 低電阻(高壓)
                new(500.0,    5.0,    8.0),   // 觸發點
                new(300.0,   10.0,    6.0),
                new(150.0,   20.0,    5.0),
                new( 80.0,   50.0,    4.0),
                new( 40.0,  100.0,    3.5),
                new( 16.0,  150.0,    3.0),   // 曲線陡降區
                new( 10.0,  200.0,    3.0),
                new(  6.0,  300.0,    2.5),
                new(  4.0,  400.0,    2.5),
                new(  3.0,  500.0,    2.0),
                new(  2.2,  600.0,    2.0),
                new(  1.6,  700.0,    2.0),   // 曲線趨緩區
                new(  1.2,  800.0,    2.5),
                new(  0.8,  900.0,    3.0),
                new(  0.5, 1000.0,    3.5),   // 最大量程
                new(  0.2, 1000.0,    5.0),   // 飽和區
            }
        };
        _recipes["STD-001"] = universal;

        // ── Recipe 2: 低壓精密校正 (Low Force Precision) ──
        // 針對 5g-200g 範圍優化，適合精密觸控、薄膜按鍵測試
        var lowForce = new CalibrationRecipe
        {
            RecipeId = "LFP-001",
            Name = "低壓精密校正 (5-200g)",
            Description = "針對低壓力範圍優化的校正配方，適用於觸控面板、薄膜按鍵測試",
            SensorSerialNumber = "UNIVERSAL",
            Interpolation = InterpolationMethod.CubicSpline,
            LookupTable = new List<CalibrationPoint>
            {
                new(500.0,    5.0,    5.0),
                new(450.0,    6.0,    4.5),
                new(400.0,    7.5,    4.0),
                new(350.0,    9.0,    3.5),
                new(300.0,   10.0,    3.0),
                new(250.0,   12.0,    2.5),
                new(200.0,   15.0,    2.0),
                new(150.0,   20.0,    2.0),
                new(120.0,   25.0,    2.0),
                new(100.0,   30.0,    2.0),
                new( 80.0,   40.0,    2.0),
                new( 60.0,   60.0,    2.0),
                new( 40.0,  100.0,    2.5),
                new( 25.0,  150.0,    3.0),
                new( 16.0,  200.0,    3.5),
                new(  0.2, 1000.0,    8.0),
            }
        };
        _recipes["LFP-001"] = lowForce;

        // ── Recipe 3: 高壓線性校正 (High Force Linear) ──
        // 針對 200g-1000g 範圍，適合咬合力、座椅壓力測試
        var highForce = new CalibrationRecipe
        {
            RecipeId = "HFL-001",
            Name = "高壓線性校正 (200-1000g)",
            Description = "針對高壓力範圍優化的校正配方，適用於咬合力測量、步態分析",
            SensorSerialNumber = "UNIVERSAL",
            Interpolation = InterpolationMethod.Linear,
            LookupTable = new List<CalibrationPoint>
            {
                new(500.0,    5.0,   10.0),
                new( 16.0,  200.0,    5.0),
                new( 10.0,  250.0,    3.0),
                new(  7.0,  300.0,    2.5),
                new(  5.5,  350.0,    2.0),
                new(  4.5,  400.0,    2.0),
                new(  3.8,  450.0,    2.0),
                new(  3.2,  500.0,    2.0),
                new(  2.7,  550.0,    2.0),
                new(  2.3,  600.0,    2.0),
                new(  2.0,  650.0,    2.0),
                new(  1.7,  700.0,    2.0),
                new(  1.4,  750.0,    2.5),
                new(  1.1,  800.0,    2.5),
                new(  0.8,  900.0,    3.0),
                new(  0.5, 1000.0,    3.5),
                new(  0.2, 1000.0,    5.0),
            }
        };
        _recipes["HFL-001"] = highForce;

        // ── Recipe 4: 寬範圍導電度校正 (Conductance-Based) ──
        // 使用 1/R 導電度進行線性近似，適合快速量測
        var conductance = new CalibrationRecipe
        {
            RecipeId = "CON-001",
            Name = "導電度線性校正",
            Description = "基於 1/R@F 導電度曲線的線性近似校正，計算效率高，適合高速量測",
            SensorSerialNumber = "UNIVERSAL",
            Interpolation = InterpolationMethod.Linear,
            LookupTable = GenerateConductanceLookup()
        };
        _recipes["CON-001"] = conductance;

        _activeRecipe = universal;
    }

    /// <summary>
    /// 基於導電度 (1/R) 的線性模型生成校正表
    /// 根據規格書 1/R@F 曲線：導電度與壓力近似線性關係
    /// </summary>
    private static List<CalibrationPoint> GenerateConductanceLookup()
    {
        var points = new List<CalibrationPoint>();

        // 1/R@F 曲線近似：conductance ≈ a * Force + b
        // 從規格書讀取：Force=0→1/R≈0, Force=100g→1/R≈0.05, Force=1000g→1/R≈0.15 (1/KΩ)
        // 近似參數: 1/R(KΩ) = 0.00015 * F(g) + 0.01 (for F > 5g)
        double a = 0.00015;
        double b = 0.01;

        for (int f = 5; f <= 1000; f += 5)
        {
            double conductance = a * f + b;
            double resistance = 1.0 / conductance;
            points.Add(new CalibrationPoint(resistance, f, 4.0));
        }

        // 排序：電阻遞減
        points.Sort((p1, p2) => p2.ResistanceKOhm.CompareTo(p1.ResistanceKOhm));
        return points;
    }

    /// <summary>
    /// 設定使用中的校正配方
    /// </summary>
    public void SetActiveRecipe(string recipeId)
    {
        if (_recipes.TryGetValue(recipeId, out var recipe))
        {
            _activeRecipe = recipe;
            RecipeChanged?.Invoke(this, recipeId);
        }
        else
        {
            throw new KeyNotFoundException($"校正配方 '{recipeId}' 不存在");
        }
    }

    /// <summary>
    /// 將整幀電阻值轉換為壓力值
    /// </summary>
    public void CalibrateFrame(SensorFrame frame)
    {
        if (_activeRecipe == null)
            throw new InvalidOperationException("未設定校正配方");

        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                double resistance = frame.RawResistance[r, c];
                double force = _activeRecipe.ResistanceToForce(resistance);

                // 套用個別點偏移補償
                if (_activeRecipe.PointOffsetMatrix != null)
                    force *= _activeRecipe.PointOffsetMatrix[r, c];

                // 限制在有效範圍內
                frame.PressureGrams[r, c] = Math.Clamp(force, 0, SensorConfig.MaxForceGrams);
            }
        }

        frame.ComputeStatistics();
    }

    /// <summary>
    /// 新增自訂校正配方
    /// </summary>
    public void AddRecipe(CalibrationRecipe recipe)
    {
        _recipes[recipe.RecipeId] = recipe;
        SaveRecipe(recipe);
    }

    /// <summary>
    /// 建立個別感測器校正（客戶回廠校正用）
    /// 透過已知標準重量的量測數據，建立該感測器的專屬校正
    /// </summary>
    public CalibrationRecipe CreateSensorSpecificCalibration(
        string sensorSerial,
        List<(double knownForceGrams, double[,] measuredResistance)> calibrationData,
        string operatorName)
    {
        var recipe = new CalibrationRecipe
        {
            RecipeId = $"CAL-{sensorSerial}-{DateTime.Now:yyyyMMdd}",
            Name = $"專屬校正 - {sensorSerial}",
            Description = $"針對序號 {sensorSerial} 的個別校正，校正點數: {calibrationData.Count}",
            SensorSerialNumber = sensorSerial,
            CalibratedBy = operatorName,
            CalibrationTemperature = 25.0,
            Interpolation = InterpolationMethod.CubicSpline
        };

        // 計算各校正點的平均電阻值
        var lookupPoints = new List<CalibrationPoint>();
        foreach (var (force, resistance) in calibrationData)
        {
            double sumR = 0;
            int count = 0;
            for (int r = 0; r < SensorConfig.Rows; r++)
            {
                for (int c = 0; c < SensorConfig.Cols; c++)
                {
                    if (resistance[r, c] < SensorConfig.NoTouchResistanceKOhm)
                    {
                        sumR += resistance[r, c];
                        count++;
                    }
                }
            }
            if (count > 0)
                lookupPoints.Add(new CalibrationPoint(sumR / count, force, 3.0));
        }

        lookupPoints.Sort((a, b) => b.ResistanceKOhm.CompareTo(a.ResistanceKOhm));
        recipe.LookupTable = lookupPoints;

        // 計算點偏移矩陣
        recipe.PointOffsetMatrix = ComputeOffsetMatrix(recipe, calibrationData);

        AddRecipe(recipe);
        return recipe;
    }

    private double[,] ComputeOffsetMatrix(CalibrationRecipe baseRecipe,
        List<(double knownForce, double[,] measuredResistance)> data)
    {
        var offset = new double[SensorConfig.Rows, SensorConfig.Cols];

        // 初始化為 1.0 (無偏移)
        for (int r = 0; r < SensorConfig.Rows; r++)
            for (int c = 0; c < SensorConfig.Cols; c++)
                offset[r, c] = 1.0;

        // 使用中等壓力的校正數據計算偏移
        var midData = data.FirstOrDefault(d => d.knownForce >= 200 && d.knownForce <= 500);
        if (midData.measuredResistance == null) return offset;

        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                double resistance = midData.measuredResistance[r, c];
                if (resistance < SensorConfig.NoTouchResistanceKOhm)
                {
                    double calculatedForce = baseRecipe.ResistanceToForce(resistance);
                    if (calculatedForce > 0)
                        offset[r, c] = midData.knownForce / calculatedForce;
                }
            }
        }

        return offset;
    }

    private void SaveRecipe(CalibrationRecipe recipe)
    {
        string path = Path.Combine(_calibrationFolder, $"{recipe.RecipeId}.json");
        File.WriteAllText(path, recipe.ToJson());
    }

    private void LoadSavedRecipes()
    {
        foreach (var file in Directory.GetFiles(_calibrationFolder, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                var recipe = CalibrationRecipe.FromJson(json);
                if (recipe != null && !_recipes.ContainsKey(recipe.RecipeId))
                    _recipes[recipe.RecipeId] = recipe;
            }
            catch { /* Skip invalid files */ }
        }
    }
}
