using System.IO;
using System.Text.Json;

namespace PressureMappingSystem.Models;

/// <summary>
/// 使用者設定持久化 — 儲存濾波器與顯示參數，下次啟動自動還原。
///
/// JSON 路徑：%APPDATA%\TRANZX\PressureMappingSystem\Config\user_settings.json
/// </summary>
public class UserSettings
{
    // ── 濾波器模式 ──
    /// <summary>濾波器模式：0=Legacy, 1=TemporalCoherence</summary>
    public int FilterMode { get; set; } = 0;

    /// <summary>TCF 響應時間 (ms)</summary>
    public double TcfResponseMs { get; set; } = 80;

    /// <summary>TCF 雜訊抑制強度 (0~100)</summary>
    public double TcfNoiseSuppression { get; set; } = 50;

    // ── 顯示與雜訊 ──
    /// <summary>色階上限 — 以每點 g 為單位（DisplayThreshold）</summary>
    public double DisplayThresholdGrams { get; set; } = SensorConfig.MaxForceGrams;

    /// <summary>最低感測門檻 (g)</summary>
    public double NoiseFloorGrams { get; set; } = 5.0;

    /// <summary>訊號純淨度 (0~50%)</summary>
    public double NoiseFilterPercent { get; set; } = 0;

    // ── 持久化方法 ──

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 載入設定；若檔案不存在或損壞則傳回預設值（不阻擋程式啟動）。
    /// </summary>
    public static UserSettings LoadOrCreateDefault()
    {
        string path = GetDefaultPath();
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<UserSettings>(json);
                if (loaded != null) return loaded;
            }
        }
        catch
        {
            // JSON 損壞或格式不符時走預設、不擋住程式
        }

        return new UserSettings();
    }

    /// <summary>
    /// 將目前設定寫入 JSON 檔。
    /// </summary>
    public void Save()
    {
        string path = GetDefaultPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, _jsonOptions));
        }
        catch
        {
            // 寫入失敗不影響程式運作
        }
    }

    /// <summary>
    /// 預設 JSON 檔路徑。
    /// </summary>
    public static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "TRANZX", "PressureMappingSystem", "Config", "user_settings.json");
    }
}
