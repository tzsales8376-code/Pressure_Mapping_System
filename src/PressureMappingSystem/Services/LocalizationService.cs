using System.Collections.ObjectModel;
using System.Globalization;

namespace PressureMappingSystem.Services;

/// <summary>
/// 多語系服務 (i18n)
/// 支援：繁體中文 (zh-TW)、英文 (en-US)、日文 (ja-JP)
/// </summary>
public class LocalizationService
{
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    private string _currentLanguage = "zh-TW";
    private readonly Dictionary<string, Dictionary<string, string>> _resources = new();

    public string CurrentLanguage => _currentLanguage;
    public event EventHandler<string>? LanguageChanged;

    public static ReadOnlyCollection<LanguageInfo> AvailableLanguages { get; } = new(new List<LanguageInfo>
    {
        new("zh-TW", "繁體中文", "Traditional Chinese"),
        new("en-US", "English", "English"),
        new("ja-JP", "日本語", "Japanese"),
    });

    private LocalizationService()
    {
        LoadChineseTraditional();
        LoadEnglish();
        LoadJapanese();
    }

    /// <summary>
    /// 取得翻譯字串
    /// </summary>
    public string Get(string key)
    {
        if (_resources.TryGetValue(_currentLanguage, out var dict) &&
            dict.TryGetValue(key, out var value))
            return value;

        // Fallback to English
        if (_resources.TryGetValue("en-US", out var enDict) &&
            enDict.TryGetValue(key, out var enValue))
            return enValue;

        return $"[{key}]"; // Missing key indicator
    }

    /// <summary>
    /// 取得翻譯字串 (快捷靜態方法)
    /// </summary>
    public static string T(string key) => Instance.Get(key);

    /// <summary>
    /// 切換語言
    /// </summary>
    public void SetLanguage(string languageCode)
    {
        if (!_resources.ContainsKey(languageCode)) return;
        _currentLanguage = languageCode;
        CultureInfo.CurrentUICulture = new CultureInfo(languageCode);
        LanguageChanged?.Invoke(this, languageCode);
    }

    // ═══════════════════════════════════════════
    // 繁體中文 (zh-TW)
    // ═══════════════════════════════════════════
    private void LoadChineseTraditional()
    {
        var d = new Dictionary<string, string>
        {
            // ── App ──
            ["App.Title"] = "TRANZX 壓力分佈量測系統",
            ["App.Subtitle"] = "壓力分佈感測器",
            ["App.Copyright"] = "© 2026 TRANZX Technology",
            ["App.Version"] = "v1.1",

            // ── Menu / Toolbar ──
            ["Toolbar.ComPort"] = "COM Port:",
            ["Toolbar.Refresh"] = "重新整理",
            ["Toolbar.Connect"] = "連接",
            ["Toolbar.Disconnect"] = "斷開",
            ["Toolbar.Simulation"] = "模擬:",
            ["Toolbar.StartSim"] = "▶ 啟動模擬",
            ["Toolbar.StopSim"] = "■ 停止",
            ["Toolbar.CalRecipe"] = "校正配方:",
            ["Toolbar.2DHeatmap"] = "2D 熱力圖",
            ["Toolbar.2DContour"] = "2D 等高線",
            ["Toolbar.3DSurface"] = "3D 表面圖",
            ["Toolbar.TrendChart"] = "趨勢圖",
            ["Toolbar.Language"] = "語言:",

            // ── Simulation Modes ──
            ["SimMode.StaticCenter"] = "靜態中心",
            ["SimMode.MovingPressure"] = "移動壓力",
            ["SimMode.PulsingPressure"] = "脈衝壓力",
            ["SimMode.MultiPoint"] = "多點壓力",
            ["SimMode.FullSurface"] = "全面壓力",
            ["SimMode.RandomNoise"] = "隨機雜訊",

            // ── Statistics ──
            ["Stats.Title"] = "即時統計",
            ["Stats.PeakPressure"] = "峰值壓力:",
            ["Stats.TotalForce"] = "總壓力:",
            ["Stats.AvgPressure"] = "平均壓力:",
            ["Stats.ActivePoints"] = "觸發點數:",
            ["Stats.ContactArea"] = "接觸面積:",
            ["Stats.CoP"] = "壓力重心:",

            // ── Peak ──
            ["Peak.Title"] = "PEAK 峰值追蹤",
            ["Peak.MaxPressure"] = "會話最大壓力:",
            ["Peak.Position"] = "Peak 位置:",
            ["Peak.Reset"] = "重置 Peak",
            ["Peak.ShowMap"] = "顯示 Peak Map",

            // ── Recording ──
            ["Record.Record"] = "⏺ 錄製",
            ["Record.Stop"] = "⏹ 停止",
            ["Record.Duration"] = "錄製:",
            ["Record.Frames"] = "幀:",
            ["Record.Title"] = "錄製管理",
            ["Record.Save"] = "儲存",
            ["Record.Load"] = "載入",

            // ── Export ──
            ["Export.Title"] = "匯出",
            ["Export.CSV"] = "匯出 CSV",
            ["Export.PNG"] = "匯出 PNG",
            ["Export.JPG"] = "匯出 JPG",
            ["Export.MP4"] = "匯出 MP4",
            ["Export.PDF"] = "匯出報告 (PDF)",

            // ── ROI ──
            ["ROI.Title"] = "ROI 區域分析",
            ["ROI.SelectMode"] = "選取模式",
            ["ROI.Rectangle"] = "矩形",
            ["ROI.Ellipse"] = "橢圓",
            ["ROI.FreeForm"] = "自由形狀",
            ["ROI.Clear"] = "清除 ROI",
            ["ROI.Stats"] = "區域統計:",

            // ── Sensor Info ──
            ["SensorInfo.Title"] = "感測器資訊",
            ["SensorInfo.Model"] = "型號:",
            ["SensorInfo.Array"] = "陣列:",
            ["SensorInfo.Pitch"] = "間距:",
            ["SensorInfo.Range"] = "壓力範圍:",
            ["SensorInfo.Resistance"] = "電阻範圍:",
            ["SensorInfo.Comm"] = "通訊:",
            ["SensorInfo.Response"] = "響應時間:",

            // ── Status ──
            ["Status.Ready"] = "就緒 - 請連接裝置或啟動模擬",
            ["Status.Connected"] = "已連接",
            ["Status.Disconnected"] = "已斷開",
            ["Status.Simulating"] = "模擬中",
            ["Status.Recording"] = "錄製中...",
            ["Status.DeviceDetected"] = "偵測到裝置:",
            ["Status.DeviceRemoved"] = "裝置已移除:",
            ["Status.Scanning"] = "掃描裝置中...",

            // ── PDF Report ──
            ["Report.Title"] = "壓力量測報告",
            ["Report.Company"] = "TRANZX Technology",
            ["Report.Generated"] = "報告產生日期:",
            ["Report.Operator"] = "操作員:",
            ["Report.Sensor"] = "感測器型號:",
            ["Report.CalRecipe"] = "校正配方:",
            ["Report.Summary"] = "量測摘要",
            ["Report.PeakPressure"] = "峰值壓力",
            ["Report.TotalForce"] = "總壓力",
            ["Report.ContactArea"] = "接觸面積",
            ["Report.ActivePoints"] = "觸發點數",
            ["Report.Heatmap"] = "壓力分佈圖",
            ["Report.Calibration"] = "校正資訊",
        };
        _resources["zh-TW"] = d;
    }

    // ═══════════════════════════════════════════
    // English (en-US)
    // ═══════════════════════════════════════════
    private void LoadEnglish()
    {
        var d = new Dictionary<string, string>
        {
            ["App.Title"] = "TRANZX Pressure Mapping System",
            ["App.Subtitle"] = "Pressure Distribution Sensor",
            ["App.Copyright"] = "© 2026 TRANZX Technology",
            ["App.Version"] = "v1.1",

            ["Toolbar.ComPort"] = "COM Port:",
            ["Toolbar.Refresh"] = "Refresh",
            ["Toolbar.Connect"] = "Connect",
            ["Toolbar.Disconnect"] = "Disconnect",
            ["Toolbar.Simulation"] = "Simulation:",
            ["Toolbar.StartSim"] = "▶ Start Sim",
            ["Toolbar.StopSim"] = "■ Stop",
            ["Toolbar.CalRecipe"] = "Cal. Recipe:",
            ["Toolbar.2DHeatmap"] = "2D Heatmap",
            ["Toolbar.2DContour"] = "2D Contour",
            ["Toolbar.3DSurface"] = "3D Surface",
            ["Toolbar.TrendChart"] = "Trend Chart",
            ["Toolbar.Language"] = "Language:",

            ["SimMode.StaticCenter"] = "Static Center",
            ["SimMode.MovingPressure"] = "Moving Pressure",
            ["SimMode.PulsingPressure"] = "Pulsing Pressure",
            ["SimMode.MultiPoint"] = "Multi-Point",
            ["SimMode.FullSurface"] = "Full Surface",
            ["SimMode.RandomNoise"] = "Random Noise",

            ["Stats.Title"] = "Real-time Statistics",
            ["Stats.PeakPressure"] = "Peak Pressure:",
            ["Stats.TotalForce"] = "Total Force:",
            ["Stats.AvgPressure"] = "Avg Pressure:",
            ["Stats.ActivePoints"] = "Active Points:",
            ["Stats.ContactArea"] = "Contact Area:",
            ["Stats.CoP"] = "Center of Pressure:",

            ["Peak.Title"] = "PEAK Tracking",
            ["Peak.MaxPressure"] = "Session Max Pressure:",
            ["Peak.Position"] = "Peak Position:",
            ["Peak.Reset"] = "Reset Peak",
            ["Peak.ShowMap"] = "Show Peak Map",

            ["Record.Record"] = "⏺ Record",
            ["Record.Stop"] = "⏹ Stop",
            ["Record.Duration"] = "Duration:",
            ["Record.Frames"] = "Frames:",
            ["Record.Title"] = "Recording Management",
            ["Record.Save"] = "Save",
            ["Record.Load"] = "Load",

            ["Export.Title"] = "Export",
            ["Export.CSV"] = "Export CSV",
            ["Export.PNG"] = "Export PNG",
            ["Export.JPG"] = "Export JPG",
            ["Export.MP4"] = "Export MP4",
            ["Export.PDF"] = "Export Report (PDF)",

            ["ROI.Title"] = "ROI Region Analysis",
            ["ROI.SelectMode"] = "Selection Mode",
            ["ROI.Rectangle"] = "Rectangle",
            ["ROI.Ellipse"] = "Ellipse",
            ["ROI.FreeForm"] = "Free Form",
            ["ROI.Clear"] = "Clear ROI",
            ["ROI.Stats"] = "Region Statistics:",

            ["SensorInfo.Title"] = "Sensor Information",
            ["SensorInfo.Model"] = "Model:",
            ["SensorInfo.Array"] = "Array:",
            ["SensorInfo.Pitch"] = "Pitch:",
            ["SensorInfo.Range"] = "Pressure Range:",
            ["SensorInfo.Resistance"] = "Resistance Range:",
            ["SensorInfo.Comm"] = "Communication:",
            ["SensorInfo.Response"] = "Response Time:",

            ["Status.Ready"] = "Ready - Connect device or start simulation",
            ["Status.Connected"] = "Connected",
            ["Status.Disconnected"] = "Disconnected",
            ["Status.Simulating"] = "Simulating",
            ["Status.Recording"] = "Recording...",
            ["Status.DeviceDetected"] = "Device detected:",
            ["Status.DeviceRemoved"] = "Device removed:",
            ["Status.Scanning"] = "Scanning devices...",

            ["Report.Title"] = "Pressure Measurement Report",
            ["Report.Company"] = "TRANZX Technology",
            ["Report.Generated"] = "Report Generated:",
            ["Report.Operator"] = "Operator:",
            ["Report.Sensor"] = "Sensor Model:",
            ["Report.CalRecipe"] = "Calibration Recipe:",
            ["Report.Summary"] = "Measurement Summary",
            ["Report.PeakPressure"] = "Peak Pressure",
            ["Report.TotalForce"] = "Total Force",
            ["Report.ContactArea"] = "Contact Area",
            ["Report.ActivePoints"] = "Active Points",
            ["Report.Heatmap"] = "Pressure Distribution Map",
            ["Report.Calibration"] = "Calibration Information",
        };
        _resources["en-US"] = d;
    }

    // ═══════════════════════════════════════════
    // 日本語 (ja-JP)
    // ═══════════════════════════════════════════
    private void LoadJapanese()
    {
        var d = new Dictionary<string, string>
        {
            ["App.Title"] = "TRANZX 圧力分布測定システム",
            ["App.Subtitle"] = "圧力分布センサー",
            ["App.Copyright"] = "© 2026 TRANZX Technology",
            ["App.Version"] = "v1.1",

            ["Toolbar.ComPort"] = "COMポート:",
            ["Toolbar.Refresh"] = "更新",
            ["Toolbar.Connect"] = "接続",
            ["Toolbar.Disconnect"] = "切断",
            ["Toolbar.Simulation"] = "シミュレーション:",
            ["Toolbar.StartSim"] = "▶ 開始",
            ["Toolbar.StopSim"] = "■ 停止",
            ["Toolbar.CalRecipe"] = "校正レシピ:",
            ["Toolbar.2DHeatmap"] = "2Dヒートマップ",
            ["Toolbar.2DContour"] = "2D等高線",
            ["Toolbar.3DSurface"] = "3D表面図",
            ["Toolbar.TrendChart"] = "トレンド",
            ["Toolbar.Language"] = "言語:",

            ["SimMode.StaticCenter"] = "静的中心",
            ["SimMode.MovingPressure"] = "移動圧力",
            ["SimMode.PulsingPressure"] = "パルス圧力",
            ["SimMode.MultiPoint"] = "マルチポイント",
            ["SimMode.FullSurface"] = "全面圧力",
            ["SimMode.RandomNoise"] = "ランダムノイズ",

            ["Stats.Title"] = "リアルタイム統計",
            ["Stats.PeakPressure"] = "ピーク圧力:",
            ["Stats.TotalForce"] = "総圧力:",
            ["Stats.AvgPressure"] = "平均圧力:",
            ["Stats.ActivePoints"] = "有効ポイント:",
            ["Stats.ContactArea"] = "接触面積:",
            ["Stats.CoP"] = "圧力中心:",

            ["Peak.Title"] = "PEAK ピーク追跡",
            ["Peak.MaxPressure"] = "セッション最大圧力:",
            ["Peak.Position"] = "ピーク位置:",
            ["Peak.Reset"] = "ピークリセット",
            ["Peak.ShowMap"] = "ピークマップ表示",

            ["Record.Record"] = "⏺ 録画",
            ["Record.Stop"] = "⏹ 停止",
            ["Record.Duration"] = "録画時間:",
            ["Record.Frames"] = "フレーム:",
            ["Record.Title"] = "録画管理",
            ["Record.Save"] = "保存",
            ["Record.Load"] = "読込",

            ["Export.Title"] = "エクスポート",
            ["Export.CSV"] = "CSV出力",
            ["Export.PNG"] = "PNG出力",
            ["Export.JPG"] = "JPG出力",
            ["Export.MP4"] = "MP4出力",
            ["Export.PDF"] = "レポート出力 (PDF)",

            ["ROI.Title"] = "ROI 領域分析",
            ["ROI.SelectMode"] = "選択モード",
            ["ROI.Rectangle"] = "矩形",
            ["ROI.Ellipse"] = "楕円",
            ["ROI.FreeForm"] = "自由形状",
            ["ROI.Clear"] = "ROIクリア",
            ["ROI.Stats"] = "領域統計:",

            ["SensorInfo.Title"] = "センサー情報",
            ["SensorInfo.Model"] = "型番:",
            ["SensorInfo.Array"] = "アレイ:",
            ["SensorInfo.Pitch"] = "ピッチ:",
            ["SensorInfo.Range"] = "圧力範囲:",
            ["SensorInfo.Resistance"] = "抵抗範囲:",
            ["SensorInfo.Comm"] = "通信:",
            ["SensorInfo.Response"] = "応答時間:",

            ["Status.Ready"] = "準備完了 - デバイスを接続またはシミュレーションを開始",
            ["Status.Connected"] = "接続済み",
            ["Status.Disconnected"] = "切断済み",
            ["Status.Simulating"] = "シミュレーション中",
            ["Status.Recording"] = "録画中...",
            ["Status.DeviceDetected"] = "デバイス検出:",
            ["Status.DeviceRemoved"] = "デバイス取外し:",
            ["Status.Scanning"] = "デバイススキャン中...",

            ["Report.Title"] = "圧力測定レポート",
            ["Report.Company"] = "TRANZX Technology",
            ["Report.Generated"] = "レポート生成日:",
            ["Report.Operator"] = "オペレーター:",
            ["Report.Sensor"] = "センサー型番:",
            ["Report.CalRecipe"] = "校正レシピ:",
            ["Report.Summary"] = "測定サマリー",
            ["Report.PeakPressure"] = "ピーク圧力",
            ["Report.TotalForce"] = "総圧力",
            ["Report.ContactArea"] = "接触面積",
            ["Report.ActivePoints"] = "有効ポイント数",
            ["Report.Heatmap"] = "圧力分布図",
            ["Report.Calibration"] = "校正情報",
        };
        _resources["ja-JP"] = d;
    }
}

public record LanguageInfo(string Code, string NativeName, string EnglishName)
{
    public override string ToString() => $"{NativeName} ({Code})";
}
