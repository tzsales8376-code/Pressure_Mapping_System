using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;

namespace PressureMappingSystem.Services;

/// <summary>
/// 感測器自動偵測服務 (Plug & Play)
///
/// 功能：
/// 1. WMI 監聽 USB 裝置插拔事件
/// 2. 自動辨識 TRANZX 壓力感測器
/// 3. 自動取得 COM Port 名稱
/// 4. 支援 Handshake 協議確認裝置身份
/// </summary>
public class DeviceDetectionService : IDisposable
{
    private ManagementEventWatcher? _insertWatcher;
    private ManagementEventWatcher? _removeWatcher;
    private CancellationTokenSource? _scanCts;

    // TRANZX 裝置識別碼 (可根據實際 USB VID/PID 調整)
    // 暫時使用通用 USB-Serial 橋接晶片的 VID
    private static readonly HashSet<string> KnownVids = new()
    {
        "2341",  // Arduino / 通用
        "1A86",  // CH340/CH341
        "0403",  // FTDI
        "10C4",  // CP210x (Silicon Labs)
        "067B",  // PL2303 (Prolific)
    };

    // Handshake 指令與預期回應
    private const string HandshakeCommand = "TRANZX_IDENTIFY\r\n";
    private const string ExpectedResponsePrefix = "TRANZX_PMS";

    public bool IsMonitoring { get; private set; }

    /// <summary>偵測到感測器裝置</summary>
    public event EventHandler<DeviceInfo>? DeviceDetected;

    /// <summary>裝置已移除</summary>
    public event EventHandler<string>? DeviceRemoved;

    /// <summary>狀態更新</summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>
    /// 啟動裝置監聽
    /// </summary>
    public void StartMonitoring()
    {
        if (IsMonitoring) return;

        try
        {
            // 監聽 USB 裝置插入
            _insertWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 2 " +
                                  "WHERE TargetInstance ISA 'Win32_PnPEntity' " +
                                  "AND TargetInstance.Name LIKE '%COM%'"));
            _insertWatcher.EventArrived += OnDeviceInserted;
            _insertWatcher.Start();

            // 監聽 USB 裝置移除
            _removeWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM __InstanceDeletionEvent WITHIN 2 " +
                                  "WHERE TargetInstance ISA 'Win32_PnPEntity' " +
                                  "AND TargetInstance.Name LIKE '%COM%'"));
            _removeWatcher.EventArrived += OnDeviceRemoved;
            _removeWatcher.Start();

            IsMonitoring = true;
            StatusChanged?.Invoke(this, "裝置監聽已啟動");

            // 初次掃描
            _ = Task.Run(ScanExistingDevices);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"裝置監聽啟動失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// 停止監聽
    /// </summary>
    public void StopMonitoring()
    {
        _insertWatcher?.Stop();
        _insertWatcher?.Dispose();
        _removeWatcher?.Stop();
        _removeWatcher?.Dispose();
        _scanCts?.Cancel();
        IsMonitoring = false;
        StatusChanged?.Invoke(this, "裝置監聽已停止");
    }

    /// <summary>
    /// 手動掃描所有已連接的 COM Port
    /// </summary>
    public async Task<List<DeviceInfo>> ScanAllDevicesAsync()
    {
        var devices = new List<DeviceInfo>();
        _scanCts = new CancellationTokenSource();

        StatusChanged?.Invoke(this, "掃描中...");

        try
        {
            // 查詢系統中的 Serial 裝置
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            var collection = searcher.Get();

            foreach (ManagementObject obj in collection)
            {
                if (_scanCts.Token.IsCancellationRequested) break;

                string? name = obj["Name"]?.ToString();
                string? deviceId = obj["DeviceID"]?.ToString();
                string? caption = obj["Caption"]?.ToString();

                if (name == null) continue;

                // 提取 COM Port 名稱
                var match = Regex.Match(name, @"\(COM(\d+)\)");
                if (!match.Success) continue;

                string portName = $"COM{match.Groups[1].Value}";

                var info = new DeviceInfo
                {
                    PortName = portName,
                    DeviceName = caption ?? name,
                    DeviceId = deviceId ?? "",
                    VendorId = ExtractVid(deviceId),
                    ProductId = ExtractPid(deviceId)
                };

                // 嘗試 Handshake 辨識
                info.IsTranzxDevice = await TryHandshakeAsync(portName, _scanCts.Token);

                if (!info.IsTranzxDevice)
                {
                    // 依 VID 判斷是否為已知的 USB-Serial 橋接晶片
                    info.IsLikelyCompatible = KnownVids.Contains(info.VendorId.ToUpper());
                }

                devices.Add(info);
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"掃描失敗: {ex.Message}");
        }

        StatusChanged?.Invoke(this, $"掃描完成 - 找到 {devices.Count} 個裝置");
        return devices;
    }

    /// <summary>
    /// 嘗試與指定 COM Port 進行 Handshake
    /// </summary>
    private async Task<bool> TryHandshakeAsync(string portName, CancellationToken ct)
    {
        try
        {
            using var port = new SerialPort
            {
                PortName = portName,
                BaudRate = Models.SensorConfig.DefaultBaudRate,
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                ReadTimeout = 1000,
                WriteTimeout = 500
            };

            port.Open();
            await Task.Delay(200, ct); // 等待裝置就緒

            // 清除緩衝區
            port.DiscardInBuffer();

            // 送出識別指令
            port.Write(HandshakeCommand);
            await Task.Delay(500, ct);

            // 嘗試讀取回應
            if (port.BytesToRead > 0)
            {
                string response = port.ReadExisting().Trim();
                if (response.StartsWith(ExpectedResponsePrefix))
                {
                    StatusChanged?.Invoke(this, $"已辨識 TRANZX 感測器於 {portName}");
                    return true;
                }
            }

            port.Close();
        }
        catch { /* Port busy or device not responding */ }

        return false;
    }

    private void ScanExistingDevices()
    {
        var task = ScanAllDevicesAsync();
        task.Wait();
        foreach (var device in task.Result)
        {
            if (device.IsTranzxDevice || device.IsLikelyCompatible)
            {
                DeviceDetected?.Invoke(this, device);
            }
        }
    }

    private void OnDeviceInserted(object sender, EventArrivedEventArgs e)
    {
        StatusChanged?.Invoke(this, "偵測到新裝置...");
        // 延遲掃描，等待驅動載入
        Task.Delay(2000).ContinueWith(_ => ScanExistingDevices());
    }

    private void OnDeviceRemoved(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var target = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            string? name = target["Name"]?.ToString();
            var match = Regex.Match(name ?? "", @"\(COM(\d+)\)");
            if (match.Success)
            {
                string port = $"COM{match.Groups[1].Value}";
                DeviceRemoved?.Invoke(this, port);
                StatusChanged?.Invoke(this, $"裝置已移除: {port}");
            }
        }
        catch { }
    }

    private static string ExtractVid(string? deviceId)
    {
        if (deviceId == null) return "";
        var match = Regex.Match(deviceId, @"VID_([0-9A-Fa-f]{4})");
        return match.Success ? match.Groups[1].Value : "";
    }

    private static string ExtractPid(string? deviceId)
    {
        if (deviceId == null) return "";
        var match = Regex.Match(deviceId, @"PID_([0-9A-Fa-f]{4})");
        return match.Success ? match.Groups[1].Value : "";
    }

    public void Dispose()
    {
        StopMonitoring();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 偵測到的裝置資訊
/// </summary>
public class DeviceInfo
{
    public string PortName { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string VendorId { get; set; } = "";
    public string ProductId { get; set; } = "";

    /// <summary>透過 Handshake 確認為 TRANZX 裝置</summary>
    public bool IsTranzxDevice { get; set; }

    /// <summary>VID 為已知的 USB-Serial 晶片</summary>
    public bool IsLikelyCompatible { get; set; }

    public override string ToString() =>
        $"{PortName} - {DeviceName}{(IsTranzxDevice ? " [TRANZX]" : IsLikelyCompatible ? " [Compatible]" : "")}";
}
