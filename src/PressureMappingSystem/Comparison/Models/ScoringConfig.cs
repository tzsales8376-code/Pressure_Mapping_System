using System.IO;
using Newtonsoft.Json;

namespace PressureMappingSystem.Comparison.Models;

/// <summary>
/// 評分配置：權重、容差、合格門檻
///
/// 從外部 JSON 讀取，比對視窗只顯示不修改（品管不能在現場隨意調）。
/// JSON 路徑：%APPDATA%\TRANZX\PressureMappingSystem\Config\scoring_config.json
///
/// 預設值對應「業界平衡型」：重量 50% + 形狀 30% + 位置 10% + 面積 10%
/// </summary>
public class ScoringConfig
{
    // ── 加權（總和應該 ≈ 1.0） ──
    public double WeightFactor { get; set; } = 0.50;
    public double ShapeFactor { get; set; } = 0.30;
    public double PositionFactor { get; set; } = 0.10;
    public double AreaFactor { get; set; } = 0.10;

    // ── 重量分項容差 ──
    /// <summary>Total Force 容差（%），超過此值該分項給 0 分</summary>
    public double WeightTolerancePercent { get; set; } = 5.0;

    // ── 位置分項容差 ──
    /// <summary>CoP 最大允許偏移（mm），超過此值該分項給 0 分</summary>
    public double MaxCopShiftMm { get; set; } = 2.0;

    // ── 判定門檻 ──
    /// <summary>合格區門檻：RawScore ≥ 此值，FinalScore 飽和為 100</summary>
    public double PassZoneThreshold { get; set; } = 95.0;
    /// <summary>合格門檻：FinalScore ≥ 此值判 PASS</summary>
    public double PassThreshold { get; set; } = 80.0;

    /// <summary>從預設路徑載入；若檔案不存在則建立預設並寫回</summary>
    public static ScoringConfig LoadOrCreateDefault()
    {
        string path = GetDefaultPath();
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonConvert.DeserializeObject<ScoringConfig>(json);
                if (loaded != null) return loaded;
            }
        }
        catch
        {
            // JSON 損壞時仍走預設、不擋住程式
        }

        // 寫一份預設讓使用者能去編輯
        var def = new ScoringConfig();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(def, Formatting.Indented));
        }
        catch
        {
            // 寫入失敗不影響運作
        }
        return def;
    }

    public static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "TRANZX", "PressureMappingSystem", "Config", "scoring_config.json");
    }

    /// <summary>權重和（用於 UI 顯示與規範化）</summary>
    [JsonIgnore]
    public double WeightSum => WeightFactor + ShapeFactor + PositionFactor + AreaFactor;
}
