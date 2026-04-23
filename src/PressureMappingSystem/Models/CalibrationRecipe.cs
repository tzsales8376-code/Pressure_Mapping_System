using Newtonsoft.Json;

namespace PressureMappingSystem.Models;

/// <summary>
/// 校正配方：定義電阻值 (KΩ) 到壓力值 (g) 的映射關係
/// 每組 Recipe 代表一個標準校正曲線，用於出廠校正與客戶回廠校正
/// </summary>
public class CalibrationRecipe
{
    /// <summary>配方唯一識別碼</summary>
    public string RecipeId { get; set; } = string.Empty;

    /// <summary>配方名稱</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>配方描述</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>建立日期</summary>
    public DateTime CreatedDate { get; set; } = DateTime.Now;

    /// <summary>最後校正日期</summary>
    public DateTime LastCalibratedDate { get; set; } = DateTime.Now;

    /// <summary>校正操作員</summary>
    public string CalibratedBy { get; set; } = string.Empty;

    /// <summary>適用的感測器序號</summary>
    public string SensorSerialNumber { get; set; } = "UNIVERSAL";

    /// <summary>校正環境溫度 (°C)</summary>
    public double CalibrationTemperature { get; set; } = 25.0;

    /// <summary>
    /// 校正查找表：電阻值(KΩ) → 壓力值(g) 的對應點
    /// 排序依電阻值遞減 (高電阻=低壓力)
    /// </summary>
    public List<CalibrationPoint> LookupTable { get; set; } = new();

    /// <summary>
    /// 每個感測點的個別偏移校正矩陣 (40x40)
    /// 用於補償各點製造差異，值為乘數因子(1.0 = 無偏移)
    /// </summary>
    public double[,]? PointOffsetMatrix { get; set; }

    /// <summary>插值方法</summary>
    public InterpolationMethod Interpolation { get; set; } = InterpolationMethod.CubicSpline;

    /// <summary>
    /// 將電阻值 (KΩ) 轉換為壓力值 (g)
    /// </summary>
    public double ResistanceToForce(double resistanceKOhm)
    {
        // 未觸發
        if (resistanceKOhm >= SensorConfig.NoTouchResistanceKOhm)
            return 0.0;

        // 超出校正範圍上限
        if (resistanceKOhm >= LookupTable[0].ResistanceKOhm)
            return LookupTable[0].ForceGrams;

        // 超出校正範圍下限
        if (resistanceKOhm <= LookupTable[^1].ResistanceKOhm)
            return LookupTable[^1].ForceGrams;

        // 查找表線性插值（電阻值遞減排列）
        for (int i = 0; i < LookupTable.Count - 1; i++)
        {
            var upper = LookupTable[i];
            var lower = LookupTable[i + 1];

            if (resistanceKOhm <= upper.ResistanceKOhm && resistanceKOhm >= lower.ResistanceKOhm)
            {
                double ratio = (upper.ResistanceKOhm - resistanceKOhm)
                             / (upper.ResistanceKOhm - lower.ResistanceKOhm);

                return Interpolation switch
                {
                    InterpolationMethod.Linear => upper.ForceGrams + ratio * (lower.ForceGrams - upper.ForceGrams),
                    InterpolationMethod.CubicSpline => CubicInterpolate(i, resistanceKOhm),
                    _ => upper.ForceGrams + ratio * (lower.ForceGrams - upper.ForceGrams)
                };
            }
        }

        return 0.0;
    }

    /// <summary>
    /// 將壓力值 (g) 轉換回電阻值 (KΩ) — 用於反向驗證
    /// </summary>
    public double ForceToResistance(double forceGrams)
    {
        if (forceGrams < SensorConfig.TriggerForceGrams)
            return SensorConfig.NoTouchResistanceKOhm;

        for (int i = 0; i < LookupTable.Count - 1; i++)
        {
            var upper = LookupTable[i];
            var lower = LookupTable[i + 1];

            if (forceGrams >= upper.ForceGrams && forceGrams <= lower.ForceGrams)
            {
                double ratio = (forceGrams - upper.ForceGrams) / (lower.ForceGrams - upper.ForceGrams);
                return upper.ResistanceKOhm - ratio * (upper.ResistanceKOhm - lower.ResistanceKOhm);
            }
        }

        return LookupTable[^1].ResistanceKOhm;
    }

    private double CubicInterpolate(int index, double x)
    {
        // Simplified cubic Hermite spline
        int n = LookupTable.Count;
        int i0 = Math.Max(0, index - 1);
        int i1 = index;
        int i2 = Math.Min(n - 1, index + 1);
        int i3 = Math.Min(n - 1, index + 2);

        double x1 = LookupTable[i1].ResistanceKOhm;
        double x2 = LookupTable[i2].ResistanceKOhm;
        double y0 = LookupTable[i0].ForceGrams;
        double y1 = LookupTable[i1].ForceGrams;
        double y2 = LookupTable[i2].ForceGrams;
        double y3 = LookupTable[i3].ForceGrams;

        double t = (x1 != x2) ? (x1 - x) / (x1 - x2) : 0;
        t = Math.Clamp(t, 0, 1);

        double m1 = (y2 - y0) / 2.0;
        double m2 = (y3 - y1) / 2.0;

        double t2 = t * t;
        double t3 = t2 * t;

        return (2 * t3 - 3 * t2 + 1) * y1
             + (t3 - 2 * t2 + t) * m1
             + (-2 * t3 + 3 * t2) * y2
             + (t3 - t2) * m2;
    }

    /// <summary>
    /// 序列化為 JSON
    /// </summary>
    public string ToJson()
    {
        return JsonConvert.SerializeObject(this, Formatting.Indented, new JsonSerializerSettings
        {
            Converters = { new Array2DConverter() }
        });
    }

    /// <summary>
    /// 從 JSON 反序列化
    /// </summary>
    public static CalibrationRecipe? FromJson(string json)
    {
        return JsonConvert.DeserializeObject<CalibrationRecipe>(json, new JsonSerializerSettings
        {
            Converters = { new Array2DConverter() }
        });
    }
}

/// <summary>
/// 校正查找表中的單一資料點
/// </summary>
public class CalibrationPoint
{
    /// <summary>電阻值 (KΩ)</summary>
    public double ResistanceKOhm { get; set; }

    /// <summary>對應壓力值 (g)</summary>
    public double ForceGrams { get; set; }

    /// <summary>該點的量測不確定度 (%)</summary>
    public double UncertaintyPercent { get; set; }

    public CalibrationPoint() { }
    public CalibrationPoint(double resistanceKOhm, double forceGrams, double uncertainty = 0)
    {
        ResistanceKOhm = resistanceKOhm;
        ForceGrams = forceGrams;
        UncertaintyPercent = uncertainty;
    }
}

public enum InterpolationMethod
{
    Linear,
    CubicSpline,
    Logarithmic
}

/// <summary>
/// 2D 陣列 JSON 序列化轉換器
/// </summary>
public class Array2DConverter : JsonConverter<double[,]>
{
    public override double[,]? ReadJson(JsonReader reader, Type objectType, double[,]? existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        var jaggedArray = serializer.Deserialize<double[][]>(reader);
        if (jaggedArray == null) return null;

        int rows = jaggedArray.Length;
        int cols = jaggedArray[0].Length;
        var result = new double[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                result[r, c] = jaggedArray[r][c];
        return result;
    }

    public override void WriteJson(JsonWriter writer, double[,]? value, JsonSerializer serializer)
    {
        if (value == null) { writer.WriteNull(); return; }
        int rows = value.GetLength(0);
        int cols = value.GetLength(1);
        var jagged = new double[rows][];
        for (int r = 0; r < rows; r++)
        {
            jagged[r] = new double[cols];
            for (int c = 0; c < cols; c++)
                jagged[r][c] = value[r, c];
        }
        serializer.Serialize(writer, jagged);
    }
}
