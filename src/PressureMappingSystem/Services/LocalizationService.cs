using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

namespace PressureMappingSystem.Services;

/// <summary>
/// 多語系服務 (i18n)
/// 支援：繁體中文 (zh-TW)、英文 (en-US)、日文 (ja-JP)
/// </summary>
public class LocalizationService : INotifyPropertyChanged
{
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    private string _currentLanguage = "zh-TW";
    private readonly Dictionary<string, Dictionary<string, string>> _resources = new();

    public string CurrentLanguage => _currentLanguage;
    public event EventHandler<string>? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Indexer — 用於 XAML Binding
    /// </summary>
    public string this[string key] => Get(key);

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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
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
            ["App.Version"] = "v2.1",

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

            // ── UI 按鈕與標籤 ──
            ["UI.StartMeasure"] = "▶ 開始量測",
            ["UI.StopMeasure"] = "■ 結束量測",
            ["UI.Zero"] = "歸零",
            ["UI.Calibration"] = "校正",
            ["UI.Freeze"] = "暫停",
            ["UI.Capture"] = "擷取",
            ["UI.Settings"] = "設定",
            ["UI.2DHeat"] = "2D 熱圖",
            ["UI.2DContour"] = "2D 等高線",
            ["UI.3D"] = "3D",
            ["UI.Trend"] = "趨勢",
            ["UI.Peak"] = "峰值",
            ["UI.Grid"] = "格線",
            ["UI.Values"] = "數值",
            ["UI.CoPLabel"] = "壓心",
            ["UI.Legend"] = "圖例",
            ["UI.Interp"] = "內插:",
            ["UI.Language"] = "語言:",
            ["UI.COM"] = "COM:",
            ["UI.Connect"] = "連接",
            ["UI.Disconnect"] = "斷開",
            ["UI.StartSim"] = "▶ 模擬",
            ["UI.StopSim"] = "■",
            ["UI.FPS"] = "FPS:",
            ["UI.Record"] = "⏺ 錄製",

            // ── 標籤頁 ──
            ["Tab.Heatmap"] = "2D 熱力圖 / 等高線",
            ["Tab.3DSurface"] = "3D 表面圖",
            ["Tab.TrendChart"] = "趨勢圖",

            // ── 右側面板 ──
            ["Panel.Stats"] = "即時統計",
            ["Panel.Peak"] = "PEAK 峰值追蹤",
            ["Panel.ROI"] = "ROI 區域分析",
            ["Panel.Export"] = "匯出",
            ["Panel.Recording"] = "錄製管理",
            ["Panel.Sensor"] = "感測器資訊",
            ["Panel.Diagnostic"] = "串列診斷",

            // ── 面板內按鈕 ──
            ["Btn.Reset"] = "重置",
            ["Btn.PeakMap"] = "峰值圖",
            ["Btn.Rect"] = "+ 矩形",
            ["Btn.Ellipse"] = "+ 橢圓",
            ["Btn.Clear"] = "清除",
            ["Btn.CSV"] = "CSV",
            ["Btn.JPG"] = "JPG",
            ["Btn.PDFReport"] = "PDF 報告",
            ["Btn.CalReport"] = "校正報告",
            ["Btn.OpenFolder"] = "開啟資料夾",
            ["Btn.SaveAVI"] = "儲存 AVI",
            ["Btn.Delete"] = "刪除",
            ["Btn.Diagnostic"] = "診斷",

            // ── 診斷面板 ──
            ["Diag.Bytes"] = "位元組:",
            ["Diag.Frames"] = "幀:",
            ["Diag.Capture64K"] = "擷取 64KB",
            ["Diag.ViewHex"] = "檢視 Hex",
            ["Diag.SaveBin"] = "儲存 .bin",

            // ── HeatmapControl 用 ──
            ["Heatmap.RealTimeStats"] = "即時統計",
            ["Heatmap.Peak"] = "Peak",
            ["Heatmap.TotalForce"] = "Total Force",
            ["Heatmap.ForceArea"] = "Force Area",
            ["Heatmap.Average"] = "Average",
            ["Heatmap.ActivePoints"] = "Active Points",
            ["Heatmap.CoPmm"] = "CoP (mm)",
            ["Heatmap.PressureUnit"] = "壓力 (gf)",
            ["Heatmap.WaitingData"] = "等待資料中...",
            ["Heatmap.ROISummary"] = "ROI 摘要",

            // ── Settings 視窗 ──
            ["Settings.Title"] = "設定",
            ["Settings.FilterMode"] = "▶ 濾波器模式 (Filter Mode)",
            ["Settings.FilterModeDesc"] = "切換影像處理演算法。Legacy 為原本的空間濾波；TCF 利用時間一致性處理動靜態混合情境。",
            ["Settings.Response"] = "響應時間 (Response):",
            ["Settings.NoiseSup"] = "雜訊抑制強度 (Noise Suppression):",
            ["Settings.DisplayRange"] = "顯示範圍 (Total):",
            ["Settings.PerPoint"] = "每點:",
            ["Settings.MinThreshold"] = "最低感測門檻 (Min Threshold):",
            ["Settings.SignalPurity"] = "訊號純淨度 (Signal Purity):",
            ["Settings.Reset"] = "重置",
            ["Settings.Close"] = "關閉",
            ["Settings.PurityHint"] = "僅 Legacy 模式有作用。TCF 模式會忽略此設定。",
            ["Settings.ResponseHint"] = "短 → 靈敏但雜訊較多；長 → 平穩但反應慢。\n60 FPS 下預設 80ms ≈ 5 幀",
            ["Settings.NoiseSupHint"] = "0 → 最保留（接近原始觀測）\n50 → 平衡（預設）\n100 → 最積極抑制（ghost 清乾淨、可能影響真邊緣）",
            ["Settings.DisplayRangeHint"] = "設定色階顯示範圍的總上限。\n1600 kg = 單點上限 1000 g（滿量程）\n500 kg = 單點上限 312.5 g\n色階圖將根據此設定自動調整刻度。",
            ["Settings.MinThresholdHint"] = "單點低於此值的讀數一律歸零。\n規格最低 5g，但 1mm 間距會產生機械串擾（約 5~15g）。\n重力量測建議：15~25g（過濾串擾又不影響精度）",
            ["Settings.PurityFullHint"] = "兩階段自適應濾波：\n① D-Level 門檻：過濾全域低壓弱訊號\n② 邊緣侵蝕：逐層移除受力區外圍的機械串擾\n建議值：3~5%（精密測壓），最高可調至 50%\n※ 僅 Legacy 模式有作用。TCF 模式會忽略此設定。",
            ["Settings.PurityOff"] = "(未啟用)",
            ["Settings.LegacyLabel"] = "Legacy — 原演算法 (erosion + inpainting)",
            ["Settings.TcfLabel"] = "TCF — 時間一致性濾波 (Temporal-Coherence, 建議)",

            // ── 底部狀態列 ──
            ["Footer.Copyright"] = "© 2026 TRANZX Technology",
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
            ["App.Version"] = "v2.1",

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

            // ── UI Buttons & Labels ──
            ["UI.StartMeasure"] = "▶ Start Measure",
            ["UI.StopMeasure"] = "■ Stop Measure",
            ["UI.Zero"] = "Zero",
            ["UI.Calibration"] = "Calibrate",
            ["UI.Freeze"] = "Pause",
            ["UI.Capture"] = "Capture",
            ["UI.Settings"] = "Settings",
            ["UI.2DHeat"] = "2D Heat",
            ["UI.2DContour"] = "2D Contour",
            ["UI.3D"] = "3D",
            ["UI.Trend"] = "Trend",
            ["UI.Peak"] = "PEAK",
            ["UI.Grid"] = "Grid",
            ["UI.Values"] = "Values",
            ["UI.CoPLabel"] = "CoP",
            ["UI.Legend"] = "Legend",
            ["UI.Interp"] = "Interp:",
            ["UI.Language"] = "Language:",
            ["UI.COM"] = "COM:",
            ["UI.Connect"] = "Connect",
            ["UI.Disconnect"] = "Disconnect",
            ["UI.StartSim"] = "▶ Sim",
            ["UI.StopSim"] = "■",
            ["UI.FPS"] = "FPS:",
            ["UI.Record"] = "⏺ REC",

            // ── Tabs ──
            ["Tab.Heatmap"] = "2D Heatmap / Contour",
            ["Tab.3DSurface"] = "3D Surface",
            ["Tab.TrendChart"] = "Trend Chart",

            // ── Right Panels ──
            ["Panel.Stats"] = "Statistics",
            ["Panel.Peak"] = "PEAK Tracking",
            ["Panel.ROI"] = "ROI Analysis",
            ["Panel.Export"] = "Export",
            ["Panel.Recording"] = "Recording",
            ["Panel.Sensor"] = "Sensor Info",
            ["Panel.Diagnostic"] = "Serial Diagnostic",

            // ── Panel Buttons ──
            ["Btn.Reset"] = "Reset",
            ["Btn.PeakMap"] = "Peak Map",
            ["Btn.Rect"] = "+ Rect",
            ["Btn.Ellipse"] = "+ Ellipse",
            ["Btn.Clear"] = "Clear",
            ["Btn.CSV"] = "CSV",
            ["Btn.JPG"] = "JPG",
            ["Btn.PDFReport"] = "PDF Report",
            ["Btn.CalReport"] = "Cal Report",
            ["Btn.OpenFolder"] = "Open Folder",
            ["Btn.SaveAVI"] = "Save AVI",
            ["Btn.Delete"] = "Delete",
            ["Btn.Diagnostic"] = "Diagnostic",

            // ── Diagnostic Panel ──
            ["Diag.Bytes"] = "Bytes:",
            ["Diag.Frames"] = "Frames:",
            ["Diag.Capture64K"] = "Capture 64KB",
            ["Diag.ViewHex"] = "View Hex",
            ["Diag.SaveBin"] = "Save .bin",

            // ── HeatmapControl ──
            ["Heatmap.RealTimeStats"] = "Real-time Stats",
            ["Heatmap.Peak"] = "Peak",
            ["Heatmap.TotalForce"] = "Total Force",
            ["Heatmap.ForceArea"] = "Force Area",
            ["Heatmap.Average"] = "Average",
            ["Heatmap.ActivePoints"] = "Active Points",
            ["Heatmap.CoPmm"] = "CoP (mm)",
            ["Heatmap.PressureUnit"] = "Pressure (gf)",
            ["Heatmap.WaitingData"] = "Waiting for data...",
            ["Heatmap.ROISummary"] = "ROI Summary",

            // ── Settings Window ──
            ["Settings.Title"] = "Settings",
            ["Settings.FilterMode"] = "▶ Filter Mode",
            ["Settings.FilterModeDesc"] = "Switch image processing algorithm. Legacy uses spatial filtering; TCF uses temporal coherence for mixed static/dynamic scenarios.",
            ["Settings.Response"] = "Response Time:",
            ["Settings.NoiseSup"] = "Noise Suppression:",
            ["Settings.DisplayRange"] = "Display Range (Total):",
            ["Settings.PerPoint"] = "Per point:",
            ["Settings.MinThreshold"] = "Min Threshold:",
            ["Settings.SignalPurity"] = "Signal Purity:",
            ["Settings.Reset"] = "Reset",
            ["Settings.Close"] = "Close",
            ["Settings.PurityHint"] = "Only effective in Legacy mode. Ignored in TCF mode.",
            ["Settings.ResponseHint"] = "Short → responsive but noisier; Long → smoother but slower.\nDefault 80ms ≈ 5 frames at 60 FPS",
            ["Settings.NoiseSupHint"] = "0 → preserve most (close to raw observation)\n50 → balanced (default)\n100 → aggressive suppression (clean ghost, may affect real edges)",
            ["Settings.DisplayRangeHint"] = "Set the color scale total upper limit.\n1600 kg = per-point limit 1000 g (full range)\n500 kg = per-point limit 312.5 g\nColor scale adjusts automatically.",
            ["Settings.MinThresholdHint"] = "Readings below this value are zeroed out.\nSpec minimum 5g, but 1mm pitch causes mechanical crosstalk (~5-15g).\nWeight measurement recommendation: 15-25g",
            ["Settings.PurityFullHint"] = "Two-stage adaptive filtering:\n① D-Level threshold: filters global low-pressure weak signals\n② Edge erosion: removes mechanical crosstalk on force area edges\nRecommended: 3-5% (precision), max 50%\n※ Only effective in Legacy mode. Ignored in TCF mode.",
            ["Settings.PurityOff"] = "(Disabled)",
            ["Settings.LegacyLabel"] = "Legacy — Original algorithm (erosion + inpainting)",
            ["Settings.TcfLabel"] = "TCF — Temporal-Coherence Filter (recommended)",

            // ── Footer ──
            ["Footer.Copyright"] = "© 2026 TRANZX Technology",
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
            ["App.Version"] = "v2.1",

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

            // ── UI ボタン・ラベル ──
            ["UI.StartMeasure"] = "▶ 測定開始",
            ["UI.StopMeasure"] = "■ 測定終了",
            ["UI.Zero"] = "ゼロ",
            ["UI.Calibration"] = "校正",
            ["UI.Freeze"] = "一時停止",
            ["UI.Capture"] = "キャプチャ",
            ["UI.Settings"] = "設定",
            ["UI.2DHeat"] = "2Dヒート",
            ["UI.2DContour"] = "2D等高線",
            ["UI.3D"] = "3D",
            ["UI.Trend"] = "トレンド",
            ["UI.Peak"] = "ピーク",
            ["UI.Grid"] = "グリッド",
            ["UI.Values"] = "数値",
            ["UI.CoPLabel"] = "圧心",
            ["UI.Legend"] = "凡例",
            ["UI.Interp"] = "補間:",
            ["UI.Language"] = "言語:",
            ["UI.COM"] = "COM:",
            ["UI.Connect"] = "接続",
            ["UI.Disconnect"] = "切断",
            ["UI.StartSim"] = "▶ シミュレ",
            ["UI.StopSim"] = "■",
            ["UI.FPS"] = "FPS:",
            ["UI.Record"] = "⏺ 録画",

            // ── タブ ──
            ["Tab.Heatmap"] = "2D ヒートマップ / 等高線",
            ["Tab.3DSurface"] = "3D 表面図",
            ["Tab.TrendChart"] = "トレンドチャート",

            // ── 右パネル ──
            ["Panel.Stats"] = "リアルタイム統計",
            ["Panel.Peak"] = "PEAK ピーク追跡",
            ["Panel.ROI"] = "ROI 領域分析",
            ["Panel.Export"] = "エクスポート",
            ["Panel.Recording"] = "録画管理",
            ["Panel.Sensor"] = "センサー情報",
            ["Panel.Diagnostic"] = "シリアル診断",

            // ── パネルボタン ──
            ["Btn.Reset"] = "リセット",
            ["Btn.PeakMap"] = "ピークマップ",
            ["Btn.Rect"] = "+ 矩形",
            ["Btn.Ellipse"] = "+ 楕円",
            ["Btn.Clear"] = "クリア",
            ["Btn.CSV"] = "CSV",
            ["Btn.JPG"] = "JPG",
            ["Btn.PDFReport"] = "PDFレポート",
            ["Btn.CalReport"] = "校正レポート",
            ["Btn.OpenFolder"] = "フォルダ開く",
            ["Btn.SaveAVI"] = "AVI保存",
            ["Btn.Delete"] = "削除",
            ["Btn.Diagnostic"] = "診断",

            // ── 診断パネル ──
            ["Diag.Bytes"] = "バイト:",
            ["Diag.Frames"] = "フレーム:",
            ["Diag.Capture64K"] = "64KBキャプチャ",
            ["Diag.ViewHex"] = "Hex表示",
            ["Diag.SaveBin"] = ".bin保存",

            // ── HeatmapControl ──
            ["Heatmap.RealTimeStats"] = "リアルタイム統計",
            ["Heatmap.Peak"] = "Peak",
            ["Heatmap.TotalForce"] = "総圧力",
            ["Heatmap.ForceArea"] = "加圧面積",
            ["Heatmap.Average"] = "平均",
            ["Heatmap.ActivePoints"] = "有効ポイント",
            ["Heatmap.CoPmm"] = "圧心 (mm)",
            ["Heatmap.PressureUnit"] = "圧力 (gf)",
            ["Heatmap.WaitingData"] = "データ待機中...",
            ["Heatmap.ROISummary"] = "ROI サマリ",

            // ── 設定ウィンドウ ──
            ["Settings.Title"] = "設定",
            ["Settings.FilterMode"] = "▶ フィルタモード",
            ["Settings.FilterModeDesc"] = "画像処理アルゴリズムを切替。Legacyは空間フィルタ、TCFは時間一貫性フィルタ。",
            ["Settings.Response"] = "応答時間:",
            ["Settings.NoiseSup"] = "ノイズ抑制強度:",
            ["Settings.DisplayRange"] = "表示範囲 (Total):",
            ["Settings.PerPoint"] = "各点:",
            ["Settings.MinThreshold"] = "最低閾値:",
            ["Settings.SignalPurity"] = "信号純度:",
            ["Settings.Reset"] = "リセット",
            ["Settings.Close"] = "閉じる",
            ["Settings.PurityHint"] = "Legacyモードのみ有効。TCFでは無視されます。",
            ["Settings.ResponseHint"] = "短い → 反応が速いがノイズ多め；長い → 滑らかだが反応が遅い。\nデフォルト 80ms ≈ 60 FPS で5フレーム",
            ["Settings.NoiseSupHint"] = "0 → 最大限保持（生の観測に近い）\n50 → バランス（デフォルト）\n100 → 最大限抑制（ゴースト除去、実際のエッジに影響の可能性あり）",
            ["Settings.DisplayRangeHint"] = "カラースケールの総上限を設定。\n1600 kg = 各点上限 1000 g（フルレンジ）\n500 kg = 各点上限 312.5 g\nカラースケールは自動調整されます。",
            ["Settings.MinThresholdHint"] = "この値以下の読取値はゼロにします。\n仕様最低 5g、但し 1mm ピッチで機械的クロストーク（約5~15g）が発生。\n重量測定推奨値：15~25g",
            ["Settings.PurityFullHint"] = "2段階適応フィルタ：\n① D-Level閾値：全域低圧弱信号をフィルタ\n② エッジ侵食：受力区外周の機械的クロストークを除去\n推奨値：3~5%（精密）、最大50%\n※ Legacyモードのみ有効。TCFでは無視されます。",
            ["Settings.PurityOff"] = "(無効)",
            ["Settings.LegacyLabel"] = "Legacy — 従来アルゴリズム (erosion + inpainting)",
            ["Settings.TcfLabel"] = "TCF — 時間一貫性フィルタ (推奨)",

            // ── フッター ──
            ["Footer.Copyright"] = "© 2026 TRANZX Technology",
        };
        _resources["ja-JP"] = d;
    }
}

public record LanguageInfo(string Code, string NativeName, string EnglishName)
{
    public override string ToString() => $"{NativeName} ({Code})";
}
