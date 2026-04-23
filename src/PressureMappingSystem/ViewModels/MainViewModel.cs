using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PressureMappingSystem.Controls;
using PressureMappingSystem.Models;
using PressureMappingSystem.Services;

namespace PressureMappingSystem.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
    // ── Services ──
    private readonly CalibrationService _calibrationService;
    private readonly SerialCommunicationService _serialService;
    private readonly DataSimulatorService _simulatorService;
    private readonly RecordingService _recordingService;
    private readonly ExportService _exportService;
    private readonly DeviceDetectionService _deviceDetectionService;
    private readonly PdfReportService _pdfReportService;
    private readonly RoiAnalysisService _roiService;
    private readonly DispatcherTimer _uiTimer;

    private readonly string _appDataFolder;
    private readonly string _exportFolder;

    // ── Observable State ──
    private double[,]? _currentPressureData;
    private double[,]? _peakPressureData;
    private SensorFrame? _currentFrame;
    private bool _isConnected;
    private bool _isSimulating;
    private bool _isRecording;
    private bool _showPeakMap;
    private HeatmapDisplayMode _displayMode = HeatmapDisplayMode.Heatmap;
    private string _selectedPort = "";
    private string _statusText = "就緒 - 請連接裝置或啟動模擬";
    private double _currentFps;
    private long _totalFrames;
    private string _selectedRecipeId = "STD-001";
    private SimulationMode _selectedSimMode = SimulationMode.MovingPressure;
    private bool _showGrid = true;
    private bool _showValues = true;
    private bool _showCoP = true;
    private bool _showLegend = true;
    private int _contourLevels = 10;

    // Display mode tab
    private int _selectedViewTab; // 0=Heatmap, 1=3D, 2=Trend

    // Recording / Playback
    private RecordingSession? _selectedSession;
    private int _playbackPosition;
    private int _playbackTotal;
    private string _recordingDuration = "00:00.000";
    private int _recordingFrameCount;
    private PlaybackState _playbackState = PlaybackState.Stopped;
    private double _playbackSpeed = 1.0;

    // Peak tracking
    private double _sessionPeakPressure;
    private int _sessionPeakRow;
    private int _sessionPeakCol;

    // Statistics
    private double _peakPressure;
    private double _totalForce;
    private int _activePoints;
    private double _contactArea;
    private double _averagePressure;
    private double _copX;
    private double _copY;
    private string _hoverInfo = "";

    // i18n
    private string _selectedLanguage = "zh-TW";

    // ROI
    private bool _isRoiSelectMode;
    private RoiType _selectedRoiType = RoiType.Rectangle;
    private string _roiStatsText = "";
    private int _roiStartRow, _roiStartCol;

    // Device Detection
    private bool _autoDetectEnabled = true;
    private string _detectedDeviceInfo = "";

    // Measurement flow control
    private bool _isMeasuring;
    private bool _isFrozen;  // 凍結畫面：暫停即時更新但不中斷資料接收

    // Zero / Tare + Baseline + Threshold
    private bool _isZeroEnabled;
    private double[,]? _zeroBaseline;        // 歸零基準幀（多幀平均）
    private double[,]? _zeroBaselineAccum;   // 累積暫存（加總）
    private double[,]? _zeroBaselineMax;     // 累積期間每點觀測最大值
    private double[,]? _perPointThreshold;   // 逐點動態門檻（= max × 安全倍率）
    private int _zeroAccumCount;             // 累積幀數
    private const int ZeroAverageFrames = 64;
    private const double NoiseMarginFactor = 1.3; // 安全倍率：max noise × 1.3
    private double _displayThreshold = SensorConfig.MaxForceGrams; // 色階上限 (g/point)，預設=1000g (=1600kg total)
    private double _noiseFloorGrams = 5.0; // 全域雜訊門檻下限 (可調，預設 5g)
    private double _noiseFilterPercent; // 訊號純淨度：按峰值百分比過濾邊緣雜訊 (0~50%)
    // (訊號純淨度已移除，改用自適應串擾去除)
    private int _interpolationLevel = 1; // 內插層級：1=原始, 3=3×3, 5=5×5

    // Custom Calibration
    private double[,]? _customCalFactors; // 自訂校正乘數表
    private CustomCalibrationProfile? _selectedCalProfile;
    private string _calProfileFolder = "";

    // ── Signal Filter (v2: 可切換 Legacy 或 TCF) ──
    private FilterMode _filterMode = FilterMode.Legacy;
    private double _tcfResponseMs = 80;
    private double _tcfStabilityThreshold = 15;
    private ISignalFilter _signalFilter;  // 當前使用的濾波器

    // Diagnostic
    private string _diagnosticLog = "";
    private string _rawSerialData = "";
    private long _bytesReceived;
    private bool _showDiagPanel;

    // ── Properties ──
    public double[,]? CurrentPressureData { get => _currentPressureData; set => SetProperty(ref _currentPressureData, value); }
    public double[,]? PeakPressureData { get => _peakPressureData; set => SetProperty(ref _peakPressureData, value); }
    public SensorFrame? CurrentFrame { get => _currentFrame; set => SetProperty(ref _currentFrame, value); }
    public bool IsConnected { get => _isConnected; set => SetProperty(ref _isConnected, value); }
    public bool IsSimulating { get => _isSimulating; set => SetProperty(ref _isSimulating, value); }
    public bool IsRecording { get => _isRecording; set => SetProperty(ref _isRecording, value); }
    public bool ShowPeakMap { get => _showPeakMap; set { SetProperty(ref _showPeakMap, value); UpdateDisplay(); } }
    public HeatmapDisplayMode DisplayMode { get => _displayMode; set => SetProperty(ref _displayMode, value); }
    public string SelectedPort { get => _selectedPort; set => SetProperty(ref _selectedPort, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public double CurrentFps { get => _currentFps; set => SetProperty(ref _currentFps, value); }
    public long TotalFrames { get => _totalFrames; set => SetProperty(ref _totalFrames, value); }
    public string SelectedRecipeId { get => _selectedRecipeId; set { SetProperty(ref _selectedRecipeId, value); _calibrationService.SetActiveRecipe(value); } }
    public SimulationMode SelectedSimMode { get => _selectedSimMode; set { SetProperty(ref _selectedSimMode, value); _simulatorService.Mode = value; } }
    public bool ShowGrid { get => _showGrid; set => SetProperty(ref _showGrid, value); }
    public bool ShowValues { get => _showValues; set => SetProperty(ref _showValues, value); }
    public bool ShowCoP { get => _showCoP; set => SetProperty(ref _showCoP, value); }
    public bool ShowLegend { get => _showLegend; set => SetProperty(ref _showLegend, value); }
    public int ContourLevels { get => _contourLevels; set => SetProperty(ref _contourLevels, value); }
    public int SelectedViewTab { get => _selectedViewTab; set => SetProperty(ref _selectedViewTab, value); }
    public RecordingSession? SelectedSession { get => _selectedSession; set => SetProperty(ref _selectedSession, value); }
    public int PlaybackPosition { get => _playbackPosition; set { SetProperty(ref _playbackPosition, value); if (_selectedSession != null && value >= 0 && value < _selectedSession.Frames.Count) _recordingService.SeekTo(_selectedSession, value); } }
    public int PlaybackTotal { get => _playbackTotal; set => SetProperty(ref _playbackTotal, value); }
    public string RecordingDuration { get => _recordingDuration; set => SetProperty(ref _recordingDuration, value); }
    public int RecordingFrameCount { get => _recordingFrameCount; set => SetProperty(ref _recordingFrameCount, value); }
    public PlaybackState PlaybackState { get => _playbackState; set => SetProperty(ref _playbackState, value); }
    public double PlaybackSpeed { get => _playbackSpeed; set { SetProperty(ref _playbackSpeed, value); _recordingService.PlaybackSpeed = value; } }
    public double SessionPeakPressure { get => _sessionPeakPressure; set => SetProperty(ref _sessionPeakPressure, value); }
    public int SessionPeakRow { get => _sessionPeakRow; set => SetProperty(ref _sessionPeakRow, value); }
    public int SessionPeakCol { get => _sessionPeakCol; set => SetProperty(ref _sessionPeakCol, value); }
    public double PeakPressure { get => _peakPressure; set => SetProperty(ref _peakPressure, value); }
    public double TotalForce { get => _totalForce; set => SetProperty(ref _totalForce, value); }
    public int ActivePoints { get => _activePoints; set => SetProperty(ref _activePoints, value); }
    public double ContactArea { get => _contactArea; set => SetProperty(ref _contactArea, value); }
    public double AveragePressure { get => _averagePressure; set => SetProperty(ref _averagePressure, value); }
    public double CoPX { get => _copX; set => SetProperty(ref _copX, value); }
    public double CoPY { get => _copY; set => SetProperty(ref _copY, value); }
    public string HoverInfo { get => _hoverInfo; set => SetProperty(ref _hoverInfo, value); }
    public string SelectedLanguage { get => _selectedLanguage; set { SetProperty(ref _selectedLanguage, value); LocalizationService.Instance.SetLanguage(value); OnPropertyChanged(nameof(L)); StatusText = L.Get("Status.Ready"); } }
    public bool IsRoiSelectMode { get => _isRoiSelectMode; set => SetProperty(ref _isRoiSelectMode, value); }
    public RoiType SelectedRoiType { get => _selectedRoiType; set => SetProperty(ref _selectedRoiType, value); }
    public string RoiStatsText { get => _roiStatsText; set => SetProperty(ref _roiStatsText, value); }
    public bool AutoDetectEnabled { get => _autoDetectEnabled; set { SetProperty(ref _autoDetectEnabled, value); if (value) _deviceDetectionService.StartMonitoring(); else _deviceDetectionService.StopMonitoring(); } }
    public string DetectedDeviceInfo { get => _detectedDeviceInfo; set => SetProperty(ref _detectedDeviceInfo, value); }
    public string DiagnosticLog { get => _diagnosticLog; set => SetProperty(ref _diagnosticLog, value); }
    public string RawSerialData { get => _rawSerialData; set => SetProperty(ref _rawSerialData, value); }
    public long BytesReceivedCount { get => _bytesReceived; set => SetProperty(ref _bytesReceived, value); }
    public bool ShowDiagPanel { get => _showDiagPanel; set => SetProperty(ref _showDiagPanel, value); }
    public bool IsMeasuring
    {
        get => _isMeasuring;
        set => SetProperty(ref _isMeasuring, value);
    }

    public bool IsFrozen
    {
        get => _isFrozen;
        set
        {
            if (SetProperty(ref _isFrozen, value))
            {
                StatusText = value ? "暫停中 — 可使用滾輪放大縮小檢視數值" : "即時顯示已恢復";
            }
        }
    }

    public double DisplayThreshold
    {
        get => _displayThreshold;
        set
        {
            if (SetProperty(ref _displayThreshold, value))
            {
                OnPropertyChanged(nameof(ColorScaleMaxGrams));
                _exportService.ColorScaleMax = value;
            }
        }
    }

    /// <summary>
    /// 色階上限 (g/point)：與 DisplayThreshold 同步
    /// 設定 500kg 總上限 → DisplayThreshold = 312.5 g/point → 色階 0~312.5
    /// </summary>
    public double ColorScaleMaxGrams => _displayThreshold;

    /// <summary>
    /// 訊號純淨度 (0~50%)：按當前幀峰值的百分比做動態門檻過濾
    /// 例如峰值=96g，純淨度=5% → 門檻 = 96×5%×3 = 14.4g，低於此值歸零
    /// </summary>
    public double NoiseFilterPercent
    {
        get => _noiseFilterPercent;
        set
        {
            if (SetProperty(ref _noiseFilterPercent, Math.Clamp(value, 0, 50)))
                RebuildSignalFilter();
        }
    }

    /// <summary>
    /// 單點最低感測門檻 (g)：低於此值的讀數一律歸零
    /// 預設 5g（規格書 min），可提高至 15~25g 以過濾 1mm 間距的機械串擾
    /// </summary>
    public double NoiseFloorGrams
    {
        get => _noiseFloorGrams;
        set
        {
            if (SetProperty(ref _noiseFloorGrams, Math.Clamp(value, 5, 100)))
                RebuildSignalFilter();
        }
    }

    /// <summary>
    /// 濾波器模式：Legacy (原演算法) 或 TemporalCoherence (TCF)
    /// 切換後立即重建濾波器並清除內部狀態
    /// </summary>
    public FilterMode FilterMode
    {
        get => _filterMode;
        set
        {
            if (SetProperty(ref _filterMode, value))
            {
                RebuildSignalFilter();
                StatusText = $"濾波器模式切換為：{_signalFilter.Name}";
            }
        }
    }

    /// <summary>TCF 響應時間（ms）：短 → 靈敏、長 → 更平穩</summary>
    public double TcfResponseMs
    {
        get => _tcfResponseMs;
        set
        {
            if (SetProperty(ref _tcfResponseMs, Math.Clamp(value, 30, 500)))
                RebuildSignalFilter();
        }
    }

    /// <summary>TCF 穩定度門檻：大→寬容（更多點被信任）、小→嚴格（更多點被侵蝕）</summary>
    public double TcfStabilityThreshold
    {
        get => _tcfStabilityThreshold;
        set
        {
            if (SetProperty(ref _tcfStabilityThreshold, Math.Clamp(value, 2, 50)))
                RebuildSignalFilter();
        }
    }

    /// <summary>當前濾波器名稱（供 UI 顯示）</summary>
    public string SignalFilterName => _signalFilter?.Name ?? "";

    /// <summary>重建 signal filter，同時清除 TCF 的 ring buffer（避免舊狀態影響）</summary>
    private void RebuildSignalFilter()
    {
        _signalFilter = SignalFilterFactory.Create(_filterMode, BuildFilterParameters());
        OnPropertyChanged(nameof(SignalFilterName));
    }

    private FilterParameters BuildFilterParameters() => new()
    {
        NoiseFloorGrams = _noiseFloorGrams,
        NoiseFilterPercent = _noiseFilterPercent,
        Fps = 60,
        ResponseMs = _tcfResponseMs,
        StabilityThreshold = _tcfStabilityThreshold,
    };

    /// <summary>
    /// 內插層級：1=原始(1×1), 3=3×3內插, 5=5×5內插
    /// 僅影響視覺渲染解析度，不影響力值計算
    /// </summary>
    public int InterpolationLevel
    {
        get => _interpolationLevel;
        set
        {
            int v = value;
            if (v != 1 && v != 3 && v != 5) v = 1;
            SetProperty(ref _interpolationLevel, v);
        }
    }

    public bool IsZeroEnabled
    {
        get => _isZeroEnabled;
        set
        {
            if (SetProperty(ref _isZeroEnabled, value))
            {
                if (value)
                {
                    StartBaselineCapture();
                    StatusText = $"歸零取樣中... (0/{ZeroAverageFrames})";
                }
                else
                {
                    _zeroBaseline = null;
                    _zeroBaselineAccum = null;
                    _zeroBaselineMax = null;
                    _perPointThreshold = null;
                    _zeroAccumCount = 0;
                    _signalFilter?.Reset();   // 清除時間濾波器的歷史
                    StatusText = "歸零已關閉，基線已清除";
                }
            }
        }
    }

    private void StartBaselineCapture()
    {
        _zeroBaselineAccum = new double[SensorConfig.Rows, SensorConfig.Cols];
        _zeroBaselineMax = new double[SensorConfig.Rows, SensorConfig.Cols];
        _zeroBaseline = null;
        _perPointThreshold = null;
        _zeroAccumCount = 0;
        _signalFilter?.Reset();   // 歸零採樣前先清 TCF 歷史，避免基線期資料混入
    }

    // Localization shortcut
    public LocalizationService L => LocalizationService.Instance;

    // Calibration profile selection
    public CustomCalibrationProfile? SelectedCalProfile
    {
        get => _selectedCalProfile;
        set
        {
            if (SetProperty(ref _selectedCalProfile, value) && value != null)
            {
                ApplyCalibrationProfile(value);
            }
        }
    }

    // Collections
    public ObservableCollection<string> AvailablePorts { get; } = new();
    public ObservableCollection<RecipeInfo> AvailableRecipes { get; } = new();
    public ObservableCollection<RecordingSession> RecordedSessions { get; } = new();
    public ObservableCollection<RoiRegion> RoiRegions { get; } = new();
    public ObservableCollection<CustomCalibrationProfile> AvailableCalProfiles { get; } = new();

    // ── Commands ──
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand RefreshPortsCommand { get; }
    public ICommand StartSimulationCommand { get; }
    public ICommand StopSimulationCommand { get; }
    public ICommand StartRecordingCommand { get; }
    public ICommand StopRecordingCommand { get; }
    public ICommand PlayForwardCommand { get; }
    public ICommand PlayBackwardCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopPlaybackCommand { get; }
    public ICommand StepForwardCommand { get; }
    public ICommand StepBackwardCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportPngCommand { get; }
    public ICommand ExportJpgCommand { get; }
    public ICommand ExportMp4Command { get; }
    public ICommand ExportPdfCommand { get; }
    public ICommand OpenExportFolderCommand { get; }
    public ICommand ExportCalReportCommand { get; }
    public ICommand TogglePeakCommand { get; }
    public ICommand SetHeatmapModeCommand { get; }
    public ICommand SetContourModeCommand { get; }
    public ICommand ResetPeakCommand { get; }
    public ICommand SaveSessionCommand { get; }
    public ICommand DeleteSessionCommand { get; }
    public ICommand AddRoiRectCommand { get; }
    public ICommand AddRoiEllipseCommand { get; }
    public ICommand ClearRoiCommand { get; }
    public event Action? ClearRoiRequested;
    public ICommand ScanDevicesCommand { get; }
    public ICommand ToggleDiagCommand { get; }
    public ICommand ViewRawDataCommand { get; }
    public ICommand SaveRawDataCommand { get; }
    public ICommand StartMeasurementCommand { get; }
    public ICommand StopMeasurementCommand { get; }
    public ICommand OpenCalibrationCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand CaptureRawCommand { get; }
    public ICommand FreezeCommand { get; }
    public ICommand CaptureFrameCommand { get; }

    // Trend chart reference (set from code-behind)
    public TrendChartControl? TrendChart { get; set; }

    public MainViewModel()
    {
        _appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TRANZX", "PressureMappingSystem");
        Directory.CreateDirectory(_appDataFolder);

        _exportFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TRANZX", "PressureData");
        Directory.CreateDirectory(_exportFolder);

        // Init services
        _calibrationService = new CalibrationService(Path.Combine(_appDataFolder, "Calibration"));
        _serialService = new SerialCommunicationService();
        _simulatorService = new DataSimulatorService();
        _recordingService = new RecordingService(Path.Combine(_exportFolder, "Recordings"));
        _exportService = new ExportService(_exportFolder);
        _deviceDetectionService = new DeviceDetectionService();
        _roiService = new RoiAnalysisService();

        string? logoPath = null;
        try
        {
            var uri = new Uri("pack://application:,,,/Resources/app.ico");
            // Logo bytes will be loaded from resource in PdfReportService if available
        }
        catch { }
        _pdfReportService = new PdfReportService(_exportService, _exportFolder, logoPath);

        // Initialize signal filter with default (Legacy)
        _signalFilter = SignalFilterFactory.Create(_filterMode, BuildFilterParameters());

        // Wire events
        _serialService.FrameReceived += OnFrameReceived;
        _serialService.ConnectionChanged += (_, c) => Application.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = c;
            StatusText = c ? $"{L.Get("Status.Connected")} {SelectedPort}" : L.Get("Status.Disconnected");
        });
        _serialService.ErrorOccurred += (_, msg) => Application.Current.Dispatcher.Invoke(() => StatusText = msg);
        _serialService.DiagnosticMessage += (_, msg) => Application.Current.Dispatcher.Invoke(() =>
        {
            DiagnosticLog = msg + "\n" + DiagnosticLog;
            if (DiagnosticLog.Length > 4000) DiagnosticLog = DiagnosticLog.Substring(0, 3000);
        });
        _serialService.RawDataReceived += (_, chunk) => Application.Current.Dispatcher.Invoke(() =>
        {
            BytesReceivedCount = _serialService.BytesReceived;
        });

        _simulatorService.FrameGenerated += OnFrameReceived;

        _recordingService.PlaybackFrameChanged += OnPlaybackFrame;
        _recordingService.PlaybackStateChanged += (_, s) => Application.Current.Dispatcher.Invoke(() => PlaybackState = s);
        _recordingService.RecordingStopped += (_, s) => Application.Current.Dispatcher.Invoke(() =>
        {
            RecordedSessions.Add(s);
            SelectedSession = s;
            IsRecording = false;
            StatusText = $"錄製完成 - {s.Frames.Count} 幀, {s.DurationMs / 1000:F1}秒";
        });

        // Device detection events
        _deviceDetectionService.DeviceDetected += (_, dev) => Application.Current.Dispatcher.Invoke(() =>
        {
            DetectedDeviceInfo = dev.ToString();
            if (!AvailablePorts.Contains(dev.PortName))
                AvailablePorts.Add(dev.PortName);
            SelectedPort = dev.PortName;
            StatusText = $"{L.Get("Status.DeviceDetected")} {dev.PortName}";

            // 自動連接 TRANZX 裝置
            if (dev.IsTranzxDevice && !IsConnected)
                DoConnect();
        });
        _deviceDetectionService.DeviceRemoved += (_, port) => Application.Current.Dispatcher.Invoke(() =>
        {
            if (IsConnected && SelectedPort == port)
                DoDisconnect();
            AvailablePorts.Remove(port);
            StatusText = $"{L.Get("Status.DeviceRemoved")} {port}";
        });

        // i18n event
        LocalizationService.Instance.LanguageChanged += (_, _) =>
            Application.Current.Dispatcher.Invoke(() => OnPropertyChanged(nameof(L)));

        // Commands
        ConnectCommand = new RelayCommand(DoConnect, () => !IsConnected);
        DisconnectCommand = new RelayCommand(DoDisconnect, () => IsConnected);
        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        StartSimulationCommand = new RelayCommand(DoStartSimulation, () => !IsSimulating && !IsConnected);
        StopSimulationCommand = new RelayCommand(DoStopSimulation, () => IsSimulating);
        StartRecordingCommand = new RelayCommand(DoStartRecording, () => !IsRecording && (IsConnected || IsSimulating));
        StopRecordingCommand = new RelayCommand(DoStopRecording, () => IsRecording);
        PlayForwardCommand = new RelayCommand(() => { if (SelectedSession != null) _recordingService.PlayForward(SelectedSession); });
        PlayBackwardCommand = new RelayCommand(() => { if (SelectedSession != null) _recordingService.PlayBackward(SelectedSession); });
        PauseCommand = new RelayCommand(() => _recordingService.PausePlayback());
        StopPlaybackCommand = new RelayCommand(() => _recordingService.StopPlayback());
        StepForwardCommand = new RelayCommand(() => { if (SelectedSession != null) _recordingService.StepForward(SelectedSession); });
        StepBackwardCommand = new RelayCommand(() => { if (SelectedSession != null) _recordingService.StepBackward(SelectedSession); });
        ExportCsvCommand = new RelayCommand(DoExportCsv);
        ExportPngCommand = new RelayCommand(() => DoExportImage("png"));
        ExportJpgCommand = new RelayCommand(() => DoExportImage("jpg"));
        ExportMp4Command = new RelayCommand(DoExportMp4);
        ExportPdfCommand = new RelayCommand(DoExportPdf);
        ExportCalReportCommand = new RelayCommand(DoExportCalReport);
        OpenExportFolderCommand = new RelayCommand(() => { System.Diagnostics.Process.Start("explorer.exe", _exportFolder); });
        TogglePeakCommand = new RelayCommand(() => ShowPeakMap = !ShowPeakMap);
        SetHeatmapModeCommand = new RelayCommand(() => { DisplayMode = HeatmapDisplayMode.Heatmap; SelectedViewTab = 0; });
        SetContourModeCommand = new RelayCommand(() => { DisplayMode = HeatmapDisplayMode.Contour; SelectedViewTab = 0; });
        ResetPeakCommand = new RelayCommand(DoResetPeak);
        SaveSessionCommand = new RelayCommand(DoSaveSession);
        DeleteSessionCommand = new RelayCommand(DoDeleteSession);
        AddRoiRectCommand = new RelayCommand(DoAddRoiRect);
        AddRoiEllipseCommand = new RelayCommand(DoAddRoiEllipse);
        ClearRoiCommand = new RelayCommand(() => { _roiService.ClearAll(); RoiRegions.Clear(); RoiStatsText = ""; ClearRoiRequested?.Invoke(); });
        ScanDevicesCommand = new RelayCommand(DoScanDevices);
        ToggleDiagCommand = new RelayCommand(() => ShowDiagPanel = !ShowDiagPanel);
        ViewRawDataCommand = new RelayCommand(() => { RawSerialData = _serialService.GetHexDump(); });
        SaveRawDataCommand = new RelayCommand(() => { var path = _serialService.SaveRawDataToFile(); StatusText = $"Raw data saved: {Path.GetFileName(path)}"; });
        CaptureRawCommand = new RelayCommand(() => { _serialService.StartBulkCapture(65536); StatusText = "Capturing 64KB raw data..."; });
        StartMeasurementCommand = new RelayCommand(DoStartMeasurement, () => IsConnected && !IsMeasuring);
        StopMeasurementCommand = new RelayCommand(DoStopMeasurement, () => IsMeasuring);
        OpenCalibrationCommand = new RelayCommand(DoOpenCalibration);
        OpenSettingsCommand = new RelayCommand(DoOpenSettings);
        FreezeCommand = new RelayCommand(() => IsFrozen = !IsFrozen);
        CaptureFrameCommand = new RelayCommand(DoCaptureFrame);

        // Load recipes
        foreach (var r in _calibrationService.Recipes)
            AvailableRecipes.Add(new RecipeInfo { Id = r.Key, Name = r.Value.Name, Description = r.Value.Description });

        // Load calibration profiles
        _calProfileFolder = Path.Combine(_appDataFolder, "CustomCalibration");
        LoadCalibrationProfiles();

        // UI update timer
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();

        RefreshPorts();

        // Start Plug & Play monitoring
        _deviceDetectionService.StartMonitoring();
    }

    // ── Pending frame buffer ──
    private SensorFrame? _pendingFrame;
    private readonly object _frameLock = new();
    private double[,] _runningPeakMap = new double[SensorConfig.Rows, SensorConfig.Cols];

    private void OnFrameReceived(object? sender, SensorFrame frame)
    {
        // 只在量測中或模擬中才處理幀資料
        if (!_isMeasuring && !_isSimulating) return;

        // 二進位模式的幀已在解析器中預先設定 PressureGrams，跳過 R→F 校正
        if (!frame.IsPreCalibrated)
            _calibrationService.CalibrateFrame(frame);

        // ── 歸零 (Zero) — 累積多幀取平均 + 逐點最大值 ──
        if (_zeroBaselineAccum != null && _zeroAccumCount < ZeroAverageFrames)
        {
            for (int r = 0; r < SensorConfig.Rows; r++)
                for (int c = 0; c < SensorConfig.Cols; c++)
                {
                    double v = frame.PressureGrams[r, c];
                    _zeroBaselineAccum[r, c] += v;
                    // 追蹤每點在取樣期間的最大值
                    if (v > _zeroBaselineMax![r, c])
                        _zeroBaselineMax[r, c] = v;
                }
            _zeroAccumCount++;

            if (_zeroAccumCount >= ZeroAverageFrames)
            {
                // 計算平均 baseline 和逐點動態門檻
                _zeroBaseline = new double[SensorConfig.Rows, SensorConfig.Cols];
                _perPointThreshold = new double[SensorConfig.Rows, SensorConfig.Cols];
                for (int r = 0; r < SensorConfig.Rows; r++)
                    for (int c = 0; c < SensorConfig.Cols; c++)
                    {
                        _zeroBaseline[r, c] = _zeroBaselineAccum[r, c] / ZeroAverageFrames;
                        // 逐點門檻 = 該點觀測最大值 × 安全倍率
                        // 確保任何底噪波動都被完全抑制
                        _perPointThreshold[r, c] = _zeroBaselineMax[r, c] * NoiseMarginFactor;
                    }
                _zeroBaselineAccum = null;
                _zeroBaselineMax = null;
                Application.Current.Dispatcher.Invoke(() =>
                    StatusText = "歸零完成 — 基線 + 逐點門檻已鎖定");
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() =>
                    StatusText = $"歸零取樣中... ({_zeroAccumCount}/{ZeroAverageFrames})");
            }
            return; // 取樣期間不輸出幀
        }

        // ── 套用 baseline 減除 + 逐點門檻 + 全域門檻 ──
        {
            double globalThreshold = NoiseFloorGrams;
            for (int r = 0; r < SensorConfig.Rows; r++)
                for (int c = 0; c < SensorConfig.Cols; c++)
                {
                    double raw = frame.PressureGrams[r, c];

                    // 逐點門檻：原始值未超過該點歷史最大雜訊 → 直接歸零
                    if (_perPointThreshold != null && raw <= _perPointThreshold[r, c])
                    {
                        frame.PressureGrams[r, c] = 0;
                        continue;
                    }

                    // 減去 baseline
                    if (_zeroBaseline != null)
                        raw -= _zeroBaseline[r, c];

                    // 全域門檻：變化量低於使用者設定值 → 歸零
                    if (raw < globalThreshold)
                        raw = 0;

                    frame.PressureGrams[r, c] = Math.Max(0, raw);
                }
        }

        // ══════════════════════════════════════════════════════════════════
        //  訊號濾波（可切換 Legacy 或 TCF 模式，由 SettingsWindow 設定）
        //  Legacy = 原本的 訊號純淨度 + SpatialSignalProcess 管線
        //  TCF    = Temporal-Coherence Filter（利用最近 N 幀的時間一致性）
        // ══════════════════════════════════════════════════════════════════
        _signalFilter.Process(frame.PressureGrams);


        frame.ComputeStatistics();

        // ── 自訂校正 ──
        // v2：若當前 profile 有擬合曲線（FitParameters），
        // 計算「整片 raw 總和 → 校正後總重量」的比值，等比例套用到每一點。
        // v1 fallback：若無擬合曲線但有舊格式 _customCalFactors，套用每點乘數。
        var profile = _selectedCalProfile;
        if (profile != null && profile.HasFit)
        {
            double rawSum = 0;
            for (int r = 0; r < SensorConfig.Rows; r++)
                for (int c = 0; c < SensorConfig.Cols; c++)
                    rawSum += frame.PressureGrams[r, c];

            if (rawSum > 1e-6)
            {
                double calibratedTotal = profile.ApplyCurve(rawSum);
                double scale = calibratedTotal / rawSum;
                // 限制放大倍率到合理範圍，避免壞擬合造成爆炸
                if (scale > 0 && scale < 1e4 && !double.IsNaN(scale) && !double.IsInfinity(scale))
                {
                    for (int r = 0; r < SensorConfig.Rows; r++)
                        for (int c = 0; c < SensorConfig.Cols; c++)
                            frame.PressureGrams[r, c] *= scale;
                    frame.ComputeStatistics();
                }
            }
        }
        else if (_customCalFactors != null)
        {
            for (int r = 0; r < SensorConfig.Rows; r++)
                for (int c = 0; c < SensorConfig.Cols; c++)
                    frame.PressureGrams[r, c] *= _customCalFactors[r, c];
            frame.ComputeStatistics();
        }

        for (int r = 0; r < SensorConfig.Rows; r++)
            for (int c = 0; c < SensorConfig.Cols; c++)
                if (frame.PressureGrams[r, c] > _runningPeakMap[r, c])
                    _runningPeakMap[r, c] = frame.PressureGrams[r, c];

        if (_recordingService.IsRecording)
            _recordingService.AddFrame(frame);

        lock (_frameLock) { _pendingFrame = frame; }
    }


    private void OnPlaybackFrame(object? sender, SensorFrame frame)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            CurrentFrame = frame;
            CurrentPressureData = frame.PressureGrams;
            UpdateStatistics(frame);
            PlaybackPosition = (int)frame.FrameIndex;
        });
    }

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        SensorFrame? frame;
        lock (_frameLock)
        {
            frame = _pendingFrame;
            _pendingFrame = null;
        }
        if (frame == null) return;

        // 凍結模式：持續接收資料（含錄影），但不更新顯示
        if (!_isFrozen)
        {
            CurrentFrame = frame;
            UpdateDisplay();
            UpdateStatistics(frame);

            // Update trend chart
            TrendChart?.AddDataPoint(frame);

            // Update ROI stats
            if (_roiService.Regions.Count > 0)
                UpdateRoiStats(frame);
        }

        CurrentFps = IsSimulating ? _simulatorService.CurrentFps : _serialService.CurrentFps;
        TotalFrames = IsSimulating ? frame.FrameIndex : _serialService.FramesReceived;

        if (IsRecording && _recordingService.CurrentSession != null)
        {
            var session = _recordingService.CurrentSession;
            RecordingFrameCount = session.Frames.Count;
            RecordingDuration = TimeSpan.FromMilliseconds(session.DurationMs).ToString(@"mm\:ss\.fff");
        }
    }

    private void UpdateDisplay()
    {
        if (ShowPeakMap)
        {
            var peakCopy = new double[SensorConfig.Rows, SensorConfig.Cols];
            Array.Copy(_runningPeakMap, peakCopy, _runningPeakMap.Length);
            CurrentPressureData = peakCopy;
            PeakPressureData = peakCopy;
        }
        else if (CurrentFrame != null)
        {
            CurrentPressureData = CurrentFrame.PressureGrams;
        }
    }

    private void UpdateStatistics(SensorFrame frame)
    {
        PeakPressure = frame.PeakPressureGrams;
        TotalForce = frame.TotalForceGrams;
        ActivePoints = frame.ActivePointCount;
        ContactArea = frame.ContactAreaMm2;
        AveragePressure = frame.AveragePressureGrams;
        CoPX = frame.CenterOfPressureX;
        CoPY = frame.CenterOfPressureY;

        if (frame.PeakPressureGrams > SessionPeakPressure)
        {
            SessionPeakPressure = frame.PeakPressureGrams;
            SessionPeakRow = frame.PeakRow;
            SessionPeakCol = frame.PeakCol;
        }
    }

    private void UpdateRoiStats(SensorFrame frame)
    {
        var allStats = _roiService.AnalyzeAllRegions(frame);
        var lines = allStats.Select(s =>
            $"[{s.RegionName}] Peak:{s.PeakPressureGrams:F0}g Avg:{s.AveragePressureGrams:F0}g " +
            $"Active:{s.ActivePoints}/{s.TotalPoints} Area:{s.ContactAreaMm2:F1}mm² " +
            $"Coverage:{s.CoveragePercent:F0}% StdDev:{s.StdDevPressureGrams:F1}g");
        RoiStatsText = string.Join("\n", lines);
    }

    // ── Command Handlers ──

    private void DoConnect()
    {
        _serialService.PortName = SelectedPort;
        _serialService.BaudRate = 115200;
        if (_serialService.Connect())
            StatusText = $"{L.Get("Status.Connected")} {SelectedPort} (115200, 8N1)";
    }

    private void DoDisconnect()
    {
        if (IsMeasuring) DoStopMeasurement();
        _serialService.Disconnect();
    }

    private void DoStartMeasurement()
    {
        IsMeasuring = true;
        StatusText = "量測啟動 — 如需歸零請手動按 Zero 按鈕";
    }

    private void DoStopMeasurement()
    {
        IsMeasuring = false;
        StatusText = "量測已停止 — 連線仍保持";
    }

    private void RefreshPorts()
    {
        AvailablePorts.Clear();
        foreach (var port in SerialCommunicationService.GetAvailablePorts())
            AvailablePorts.Add(port);
        if (AvailablePorts.Count > 0)
            SelectedPort = AvailablePorts[0];
    }

    private void DoStartSimulation()
    {
        _simulatorService.Mode = SelectedSimMode;
        _simulatorService.TargetFps = 60;
        _simulatorService.Start();
        IsSimulating = true;
        StatusText = $"{L.Get("Status.Simulating")} - {SelectedSimMode}";
    }

    private void DoStopSimulation()
    {
        _simulatorService.Stop();
        IsSimulating = false;
        StatusText = L.Get("Status.Disconnected");
    }

    private void DoStartRecording()
    {
        _recordingService.StartRecording(
            calibrationRecipeId: SelectedRecipeId,
            targetFps: (int)CurrentFps > 0 ? (int)CurrentFps : 60);
        IsRecording = true;
        StatusText = L.Get("Status.Recording");
    }

    private void DoStopRecording() => _recordingService.StopRecording();

    private void DoExportCsv()
    {
        if (CurrentFrame == null) { StatusText = "沒有可匯出的幀資料"; return; }
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "匯出 CSV",
                Filter = "CSV 檔案 (*.csv)|*.csv",
                FileName = $"PressureData_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                InitialDirectory = _exportFolder
            };
            if (dialog.ShowDialog() != true) return;
            // 直接寫到使用者指定的完整路徑
            var path = _exportService.ExportFrameToCsv(CurrentFrame, dialog.FileName);
            StatusText = $"CSV 匯出成功: {Path.GetFileName(path)}";
            MessageBox.Show($"CSV 已匯出至:\n{path}", "匯出成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = $"CSV 匯出失敗: {ex.Message}";
            MessageBox.Show($"匯出失敗:\n{ex.Message}", "匯出錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DoExportImage(string format)
    {
        if (CurrentFrame == null) { StatusText = "沒有可匯出的幀資料"; return; }
        try
        {
            string ext = format.ToLower() == "jpg" ? "jpg" : "png";
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = $"匯出 {ext.ToUpper()}",
                Filter = $"{ext.ToUpper()} 影像 (*.{ext})|*.{ext}",
                FileName = $"Heatmap_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}",
                InitialDirectory = _exportFolder
            };
            if (dialog.ShowDialog() != true) return;
            var path = _exportService.ExportHeatmapImage(CurrentFrame, format, filename: dialog.FileName);
            StatusText = $"{ext.ToUpper()} 匯出成功: {Path.GetFileName(path)}";
            MessageBox.Show($"{ext.ToUpper()} 已匯出至:\n{path}", "匯出成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = $"影像匯出失敗: {ex.Message}";
            MessageBox.Show($"匯出失敗:\n{ex.Message}", "匯出錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DoCaptureFrame()
    {
        if (CurrentFrame == null) { StatusText = "沒有可擷取的幀資料"; return; }
        try
        {
            // 自動儲存 JPG 至匯出資料夾，無需對話框
            string filename = $"Capture_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
            string fullPath = Path.Combine(_exportFolder, filename);
            var path = _exportService.ExportHeatmapImage(CurrentFrame, "jpg", filename: fullPath);
            StatusText = $"擷取成功: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText = $"擷取失敗: {ex.Message}";
        }
    }

    private async void DoExportMp4()
    {
        if (SelectedSession == null || SelectedSession.Frames.Count == 0) { StatusText = "請先選擇一段錄製"; return; }
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "匯出 AVI 影片",
                Filter = "AVI 影片 (*.avi)|*.avi",
                FileName = $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}.avi",
                InitialDirectory = _exportFolder
            };
            if (dialog.ShowDialog() != true) return;
            StatusText = "匯出影片中...";
            var progress = new Progress<double>(p => StatusText = $"AVI: {p:P0}");
            var path = await _exportService.ExportSessionToMp4(SelectedSession, filename: dialog.FileName, progress: progress);
            StatusText = $"影片匯出成功: {Path.GetFileName(path)}";
            MessageBox.Show($"AVI 影片已匯出至:\n{path}", "匯出成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = $"影片匯出失敗: {ex.Message}";
            MessageBox.Show($"匯出失敗:\n{ex.Message}", "匯出錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DoExportPdf()
    {
        if (CurrentFrame == null) { StatusText = "沒有可匯出的幀資料"; return; }
        var recipe = _calibrationService.ActiveRecipe;
        if (recipe == null) { StatusText = "沒有啟用的校正配方"; return; }
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "匯出 PDF 報告",
                Filter = "PDF 檔案 (*.pdf)|*.pdf",
                FileName = $"PressureReport_{DateTime.Now:yyyyMMdd_HHmmss}.pdf",
                InitialDirectory = _exportFolder
            };
            if (dialog.ShowDialog() != true) return;
            // PDF 報告可能寫到不同路徑，先生成再搬移
            var tempPath = _pdfReportService.GenerateMeasurementReport(CurrentFrame, recipe);
            if (tempPath != dialog.FileName)
            {
                File.Move(tempPath, dialog.FileName, true);
            }
            StatusText = $"PDF 匯出成功: {Path.GetFileName(dialog.FileName)}";
            MessageBox.Show($"PDF 報告已匯出至:\n{dialog.FileName}", "匯出成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = $"PDF 匯出失敗: {ex.Message}";
            MessageBox.Show($"匯出失敗:\n{ex.Message}", "匯出錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DoExportCalReport()
    {
        var recipe = _calibrationService.ActiveRecipe;
        if (recipe == null) return;
        var path = _pdfReportService.GenerateCalibrationReport(recipe);
        StatusText = $"Cal Report: {Path.GetFileName(path)}";
    }

    private void DoResetPeak()
    {
        _runningPeakMap = new double[SensorConfig.Rows, SensorConfig.Cols];
        SessionPeakPressure = 0;
        SessionPeakRow = 0;
        SessionPeakCol = 0;
    }

    private async void DoSaveSession()
    {
        if (SelectedSession == null || SelectedSession.Frames.Count == 0)
        {
            StatusText = "沒有可儲存的錄製資料";
            return;
        }

        // 預設儲存到 Recordings 子資料夾
        var recordingFolder = Path.Combine(_exportFolder, "Recordings");
        Directory.CreateDirectory(recordingFolder);

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "儲存 AVI 影片",
            Filter = "AVI 影片 (*.avi)|*.avi",
            FileName = $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}.avi",
            InitialDirectory = recordingFolder
        };
        if (dialog.ShowDialog() != true) return;

        // 開啟進度對話框
        var progressWindow = new Controls.ExportProgressWindow
        {
            Owner = Application.Current.MainWindow
        };

        int totalFrames = SelectedSession.Frames.Count;
        var session = SelectedSession;
        string savePath = dialog.FileName;

        var progress = new Progress<double>(p =>
        {
            int framesDone = (int)(p * totalFrames);
            progressWindow.UpdateProgress(p, $"正在匯出影片... ({framesDone}/{totalFrames} 幀)");
        });

        string? resultPath = null;
        string? errorMsg = null;

        var exportTask = Task.Run(async () =>
        {
            try
            {
                resultPath = await _exportService.ExportSessionToMp4(session, filename: savePath, progress: progress);
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
            }
        });

        _ = exportTask.ContinueWith(_ =>
        {
            if (resultPath != null)
                progressWindow.Complete($"匯出完成: {Path.GetFileName(resultPath)}");
            else
                progressWindow.Fail($"匯出失敗: {errorMsg}");
        }, TaskScheduler.FromCurrentSynchronizationContext());

        progressWindow.ShowDialog();

        if (resultPath != null)
        {
            StatusText = $"影片已儲存: {Path.GetFileName(resultPath)}";
            MessageBox.Show($"AVI 已儲存至:\n{resultPath}", "儲存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            StatusText = $"影片匯出失敗: {errorMsg}";
            MessageBox.Show($"儲存失敗:\n{errorMsg}", "儲存錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 刪除選取的未儲存錄影（從記憶體列表中移除，不刪除已儲存的檔案）
    /// </summary>
    private void DoDeleteSession()
    {
        if (SelectedSession == null) return;

        // 停止播放（如果正在播放）
        _recordingService.StopPlayback();

        var session = SelectedSession;
        RecordedSessions.Remove(session);
        SelectedSession = null;
        PlaybackTotal = 0;
        PlaybackPosition = 0;
        StatusText = $"已刪除錄製：{session.Name}";
    }

    // ROI
    private void DoAddRoiRect()
    {
        // 預設選取中央 10x10 區域 (使用者可在 UI 上拖曳調整)
        int n = _roiService.Regions.Count + 1;
        var roi = _roiService.AddRectangle($"ROI-{n}", 15, 15, 25, 25);
        RoiRegions.Add(roi);
    }

    private void DoAddRoiEllipse()
    {
        int n = _roiService.Regions.Count + 1;
        var roi = _roiService.AddEllipse($"ROI-{n}", 20, 20, 8, 8);
        RoiRegions.Add(roi);
    }

    /// <summary>
    /// 從熱力圖拖曳選取建立 ROI
    /// </summary>
    public void CreateRoiFromDrag(int startRow, int startCol, int endRow, int endCol)
    {
        int n = _roiService.Regions.Count + 1;
        RoiRegion roi;
        if (SelectedRoiType == RoiType.Rectangle)
            roi = _roiService.AddRectangle($"ROI-{n}", startRow, startCol, endRow, endCol);
        else
            roi = _roiService.AddEllipse($"ROI-{n}",
                (startRow + endRow) / 2, (startCol + endCol) / 2,
                Math.Abs(endRow - startRow) / 2, Math.Abs(endCol - startCol) / 2);
        RoiRegions.Add(roi);
    }

    // Device detection
    private async void DoScanDevices()
    {
        StatusText = L.Get("Status.Scanning");
        var devices = await _deviceDetectionService.ScanAllDevicesAsync();
        AvailablePorts.Clear();
        foreach (var dev in devices)
        {
            AvailablePorts.Add(dev.PortName);
            if (dev.IsTranzxDevice)
            {
                SelectedPort = dev.PortName;
                DetectedDeviceInfo = $"✓ {dev.DeviceName}";
            }
        }
        StatusText = $"掃描完成 - {devices.Count} 裝置";
    }

    public void OnPointHovered(int row, int col, double pressure)
    {
        HoverInfo = $"({row + 1},{col + 1}) {pressure:F1}g | " +
                    $"({col * SensorConfig.SensorPitchMm:F1}, {row * SensorConfig.SensorPitchMm:F1}) mm";
    }

    // ── Settings Window ──
    private void DoOpenSettings()
    {
        var settingsWindow = new Controls.SettingsWindow(this)
        {
            Owner = Application.Current.MainWindow
        };
        settingsWindow.ShowDialog();
    }

    // ── Calibration Window ──
    private void DoOpenCalibration()
    {
        var calWindow = new Controls.CalibrationWindow(this)
        {
            Owner = Application.Current.MainWindow
        };
        calWindow.ShowDialog();
    }

    /// <summary>
    /// 取得當前幀所有有效觸發點的平均壓力值（供校正視窗使用）
    /// </summary>
    public double GetCurrentAverageReading()
    {
        var frame = CurrentFrame;
        if (frame == null) return 0;
        double sum = 0; int count = 0;
        for (int r = 0; r < SensorConfig.Rows; r++)
            for (int c = 0; c < SensorConfig.Cols; c++)
                if (frame.PressureGrams[r, c] > 0)
                { sum += frame.PressureGrams[r, c]; count++; }
        return count > 0 ? sum / count : 0;
    }

    /// <summary>
    /// 取得當前幀的最大壓力值（供校正視窗使用）
    /// </summary>
    public double GetCurrentPeakReading()
    {
        var frame = CurrentFrame;
        if (frame == null) return 0;
        return frame.PeakPressureGrams;
    }

    /// <summary>
    /// 套用自訂校正表：每個權重→讀值對應關係生成全域乘數
    /// </summary>
    public void ApplyCustomCalibration(List<(double weightGrams, double preValue, double postValue)> calPoints)
    {
        if (calPoints.Count == 0)
        {
            _customCalFactors = null;
            StatusText = "自訂校正已清除";
            return;
        }

        // 計算全域校正乘數：對每個校正點，postValue / preValue = 修正因子
        // 使用最近鄰插值，根據當前壓力值套用對應的修正因子
        _customCalFactors = new double[SensorConfig.Rows, SensorConfig.Cols];
        for (int r = 0; r < SensorConfig.Rows; r++)
            for (int c = 0; c < SensorConfig.Cols; c++)
                _customCalFactors[r, c] = 1.0; // 預設不修改

        // 計算平均修正因子
        double totalFactor = 0; int validPoints = 0;
        foreach (var (_, pre, post) in calPoints)
        {
            if (pre > 0 && post > 0)
            {
                totalFactor += post / pre;
                validPoints++;
            }
        }

        if (validPoints > 0)
        {
            double avgFactor = totalFactor / validPoints;
            for (int r = 0; r < SensorConfig.Rows; r++)
                for (int c = 0; c < SensorConfig.Cols; c++)
                    _customCalFactors[r, c] = avgFactor;
        }

        StatusText = $"自訂校正已套用 ({validPoints} 個校正點)";
    }

    // ── Calibration Profile Persistence ──

    private void LoadCalibrationProfiles()
    {
        AvailableCalProfiles.Clear();

        // 加入「無校正」預設選項
        AvailableCalProfiles.Add(new CustomCalibrationProfile
        {
            Name = "(無校正)",
            Description = "不套用自訂校正"
        });

        if (!Directory.Exists(_calProfileFolder)) return;

        foreach (var file in Directory.GetFiles(_calProfileFolder, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<CustomCalibrationProfile>(json);
                if (profile != null && !string.IsNullOrWhiteSpace(profile.Name))
                    AvailableCalProfiles.Add(profile);
            }
            catch { /* skip corrupt files */ }
        }

        // 預設選「無校正」
        _selectedCalProfile = AvailableCalProfiles[0];
        OnPropertyChanged(nameof(SelectedCalProfile));
    }

    /// <summary>
    /// 儲存校正設定檔到 JSON
    /// </summary>
    public void SaveCalibrationProfile(CustomCalibrationProfile profile)
    {
        Directory.CreateDirectory(_calProfileFolder);

        // 安全檔名
        var safeName = string.Join("_", profile.Name.Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(_calProfileFolder, $"{safeName}.json");

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(profile, options);
        File.WriteAllText(filePath, json);

        // 如果已存在同名就替換，否則新增
        var existing = AvailableCalProfiles.FirstOrDefault(p => p.Name == profile.Name);
        if (existing != null)
        {
            var idx = AvailableCalProfiles.IndexOf(existing);
            AvailableCalProfiles[idx] = profile;
        }
        else
        {
            AvailableCalProfiles.Add(profile);
        }

        SelectedCalProfile = profile;
        StatusText = $"校正設定已儲存: {profile.Name}";
    }

    /// <summary>
    /// 刪除校正設定檔
    /// </summary>
    public void DeleteCalibrationProfile(CustomCalibrationProfile profile)
    {
        if (profile.Points.Count == 0 && profile.Name == "(無校正)") return; // 不能刪預設

        var safeName = string.Join("_", profile.Name.Split(Path.GetInvalidFileNameChars()));
        var filePath = Path.Combine(_calProfileFolder, $"{safeName}.json");
        if (File.Exists(filePath))
            File.Delete(filePath);

        AvailableCalProfiles.Remove(profile);
        if (SelectedCalProfile == profile)
            SelectedCalProfile = AvailableCalProfiles.FirstOrDefault();
    }

    /// <summary>
    /// 擷取當前幀的 raw sum 與 active cells — v2 校正使用的原始量測資料。
    /// </summary>
    public (double rawSum, int activeCells) GetCurrentRawReading()
    {
        var frame = CurrentFrame;
        if (frame == null) return (0, 0);
        double sum = 0;
        int active = 0;
        for (int r = 0; r < SensorConfig.Rows; r++)
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                double v = frame.PressureGrams[r, c];
                if (v > 0)
                {
                    sum += v;
                    active++;
                }
            }
        return (sum, active);
    }

    /// <summary>
    /// 校正期間暫停套用當前 profile。用於 CalibrationWindow 擷取前呼叫，避免循環套用。
    /// </summary>
    private CustomCalibrationProfile? _tempSuspendedProfile;
    public void SuspendCalibrationForCapture()
    {
        if (_selectedCalProfile != null && _tempSuspendedProfile == null)
        {
            _tempSuspendedProfile = _selectedCalProfile;
            _selectedCalProfile = null;
            OnPropertyChanged(nameof(SelectedCalProfile));
        }
    }
    public void ResumeCalibrationAfterCapture()
    {
        if (_tempSuspendedProfile != null)
        {
            _selectedCalProfile = _tempSuspendedProfile;
            _tempSuspendedProfile = null;
            OnPropertyChanged(nameof(SelectedCalProfile));
        }
    }

    /// <summary>
    /// 套用選取的校正設定檔。
    /// v2：若 profile 有擬合曲線（HasFit），處理 pipeline 會在每幀計算 scale 套用。
    /// v1 fallback：若 profile 無擬合但有 Points 資料，退回舊的單一 factor 乘數。
    /// </summary>
    public void ApplyCalibrationProfile(CustomCalibrationProfile profile)
    {
        if (profile == null || profile.Points.Count == 0)
        {
            // 「無校正」— 清除兩種表
            _customCalFactors = null;
            StatusText = "自訂校正已清除";
            return;
        }

        if (profile.HasFit)
        {
            // 新版：曲線擬合，處理 pipeline 會動態使用 profile.ApplyCurve
            _customCalFactors = null;  // 清除舊 factor 避免重複套用
            StatusText = $"已套用校正（{profile.FitMethod}）: {profile.Name}";
        }
        else
        {
            // 舊版：單一全域乘數
            double factor = profile.ComputeGlobalFactor();
            _customCalFactors = new double[SensorConfig.Rows, SensorConfig.Cols];
            for (int r = 0; r < SensorConfig.Rows; r++)
                for (int c = 0; c < SensorConfig.Cols; c++)
                    _customCalFactors[r, c] = factor;
            StatusText = $"已套用校正 (v1 legacy): {profile.Name} (factor={factor:F3})";
        }
    }

    public void Dispose()
    {
        _uiTimer.Stop();
        _serialService.Dispose();
        _simulatorService.Dispose();
        _deviceDetectionService.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class RecipeInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public override string ToString() => Name;
}
