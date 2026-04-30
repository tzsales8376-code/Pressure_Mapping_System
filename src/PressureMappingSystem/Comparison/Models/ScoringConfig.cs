using System.IO;
using Newtonsoft.Json;

namespace PressureMappingSystem.Comparison.Models;

/// <summary>
/// 評分情境 — 決定用哪套權重 + 容差。
/// </summary>
public enum ScoringScenario
{
    /// <summary>自動：依 IoU 判定（IoU ≥ AutoThresholdIou 用情境 1，否則情境 2）</summary>
    Auto = 0,

    /// <summary>情境 1：相似待測物（同種設備、同種樣品的重複量測）— 受壓面積為主</summary>
    SimilarObject = 1,

    /// <summary>情境 2：不同待測物但同重量（例如不同形狀但等重的客戶樣品）— 總重量為主</summary>
    DifferentObject = 2,
}

/// <summary>
/// 評分配置 v2 — 雙情境 profile + 自動判斷。
///
/// JSON 路徑：%APPDATA%\TRANZX\PressureMappingSystem\Config\scoring_config.json
///
/// 業界依據：
///   情境 1（相似待測物）：受壓面積為主（IoU），形狀次之（SSIM），
///                       重量低權重（重量在重複量測本就應接近、不必強調）
///   情境 2（不同待測物同重量）：總重量為主，形狀次之，
///                              位置/面積低權重（不同物件本就不同）
/// </summary>
public class ScoringConfig
{
    // ─────────────────────────────────────────
    // 情境選擇
    // ─────────────────────────────────────────
    /// <summary>使用者選的情境（Auto = 程式自動判斷）</summary>
    public ScoringScenario Scenario { get; set; } = ScoringScenario.Auto;

    /// <summary>自動判斷的 IoU 閾值：≥ 此值為情境 1，否則情境 2</summary>
    public double AutoThresholdIou { get; set; } = 0.6;

    // ─────────────────────────────────────────
    // 情境 1：相似待測物（面積為主）
    // ─────────────────────────────────────────
    public ScenarioProfile SimilarObjectProfile { get; set; } = new()
    {
        AreaFactor = 0.50,
        ShapeFactor = 0.30,
        WeightFactor = 0.15,
        PositionFactor = 0.05,
        WeightTolerancePercent = 5.0,
        MaxCopShiftMm = 2.0,
    };

    // ─────────────────────────────────────────
    // 情境 2：不同待測物（重量為主）
    // ─────────────────────────────────────────
    public ScenarioProfile DifferentObjectProfile { get; set; } = new()
    {
        WeightFactor = 0.65,
        ShapeFactor = 0.25,
        PositionFactor = 0.05,
        AreaFactor = 0.05,
        WeightTolerancePercent = 10.0,  // 不同物件容差寬鬆
        MaxCopShiftMm = 5.0,
    };

    // ─────────────────────────────────────────
    // 共同：判定門檻
    // ─────────────────────────────────────────
    /// <summary>合格區門檻：RawScore ≥ 此值，FinalScore 飽和為 100</summary>
    public double PassZoneThreshold { get; set; } = 95.0;

    /// <summary>合格門檻：FinalScore ≥ 此值判 PASS</summary>
    public double PassThreshold { get; set; } = 80.0;

    // ─────────────────────────────────────────
    // load/save
    // ─────────────────────────────────────────
    public static ScoringConfig LoadOrCreateDefault()
    {
        string path = GetDefaultPath();
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonConvert.DeserializeObject<ScoringConfig>(json);
                if (loaded != null)
                {
                    // 容錯：profile 為 null 時補預設
                    loaded.SimilarObjectProfile ??= new ScenarioProfile
                    {
                        AreaFactor = 0.50, ShapeFactor = 0.30,
                        WeightFactor = 0.15, PositionFactor = 0.05,
                        WeightTolerancePercent = 5.0, MaxCopShiftMm = 2.0,
                    };
                    loaded.DifferentObjectProfile ??= new ScenarioProfile
                    {
                        WeightFactor = 0.65, ShapeFactor = 0.25,
                        PositionFactor = 0.05, AreaFactor = 0.05,
                        WeightTolerancePercent = 10.0, MaxCopShiftMm = 5.0,
                    };
                    return loaded;
                }
            }
        }
        catch
        {
            // JSON 損壞時走預設、不擋住程式
        }

        var def = new ScoringConfig();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(def, Formatting.Indented));
        }
        catch
        {
            // 寫入失敗不影響
        }
        return def;
    }

    public static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "TRANZX", "PressureMappingSystem", "Config", "scoring_config.json");
    }

    /// <summary>取得「實際生效」的 profile（自動模式時依 IoU 判斷）</summary>
    public ScenarioProfile ResolveProfile(ScoringScenario applied)
    {
        return applied switch
        {
            ScoringScenario.SimilarObject => SimilarObjectProfile,
            ScoringScenario.DifferentObject => DifferentObjectProfile,
            _ => SimilarObjectProfile,  // fallback
        };
    }
}

/// <summary>
/// 單一情境的權重 + 容差設定
/// </summary>
public class ScenarioProfile
{
    // 加權（總和應 ≈ 1.0，Service 會自動正規化）
    public double WeightFactor { get; set; }
    public double ShapeFactor { get; set; }
    public double PositionFactor { get; set; }
    public double AreaFactor { get; set; }

    // 容差
    public double WeightTolerancePercent { get; set; }
    public double MaxCopShiftMm { get; set; }

    [JsonIgnore]
    public double WeightSum => WeightFactor + ShapeFactor + PositionFactor + AreaFactor;
}
