using System.IO;
using System.IO.Ports;
using System.Text;
using PressureMappingSystem.Models;

namespace PressureMappingSystem.Services;

/// <summary>
/// Serial 通訊服務：透過 USB COM Port 讀取 PMS-1600 感測器資料
///
/// 支援多種解析模式：
///   Mode 1 (ASCII-FRAME): "FRAME:N" / "ROW:R:v1,v2,...,v40" / "END"
///   Mode 2 (ASCII-CSV):   每行 40 個逗號分隔數值，連續 40 行為一幀
///   Mode 3 (Raw Hex):     二進位模式 (每點 2 bytes, big-endian)
///   Mode 4 (Auto-Detect): 自動偵測格式
///
/// 診斷模式：可即時顯示原始資料以協助除錯
/// </summary>
public class SerialCommunicationService : IDisposable
{
    private SerialPort? _serialPort;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private readonly object _lock = new();

    // 通訊設定
    public string PortName { get; set; } = "COM3";
    public int BaudRate { get; set; } = SensorConfig.DefaultBaudRate;
    public Parity Parity { get; set; } = Parity.None;
    public int DataBits { get; set; } = 8;
    public StopBits StopBits { get; set; } = StopBits.One;

    // 解析模式
    public SerialParseMode ParseMode { get; set; } = SerialParseMode.AutoDetect;

    // 狀態
    public bool IsConnected => _serialPort?.IsOpen ?? false;
    public long FramesReceived { get; private set; }
    public long BytesReceived { get; private set; }
    public double CurrentFps { get; private set; }
    public string LastError { get; private set; } = string.Empty;

    // 診斷
    public bool DiagnosticMode { get; set; } = true;
    private readonly StringBuilder _rawBuffer = new();    // hex dump
    private readonly StringBuilder _rawAscii = new();     // printable ASCII
    private readonly object _diagLock = new();
    private const int MaxDiagBufferSize = 16384;
    private byte[] _recentBytes = Array.Empty<byte>();    // last N raw bytes

    // 事件
    public event EventHandler<SensorFrame>? FrameReceived;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<string>? RawDataReceived;
    public event EventHandler<string>? DiagnosticMessage;

    public static string[] GetAvailablePorts() => SerialPort.GetPortNames();

    public bool Connect()
    {
        try
        {
            Disconnect();

            _serialPort = new SerialPort
            {
                PortName = PortName,
                BaudRate = BaudRate,
                Parity = Parity,
                DataBits = DataBits,
                StopBits = StopBits,
                ReadTimeout = 3000,
                WriteTimeout = 1000,
                ReadBufferSize = 65536,
                ReceivedBytesThreshold = 1,
                Encoding = Encoding.ASCII,
                NewLine = "\n"
            };

            _serialPort.Open();

            // 清空緩衝區
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();

            FramesReceived = 0;
            BytesReceived = 0;

            lock (_diagLock) { _rawBuffer.Clear(); }

            // 啟動非同步讀取
            _readCts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoop(_readCts.Token));

            ConnectionChanged?.Invoke(this, true);
            RaiseDiag($"已連接 {PortName} ({BaudRate}, 8N1)");
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            ErrorOccurred?.Invoke(this, $"連接失敗: {ex.Message}");
            return false;
        }
    }

    public void Disconnect()
    {
        _readCts?.Cancel();
        _readTask?.Wait(TimeSpan.FromSeconds(2));
        _readCts?.Dispose();
        _readCts = null;

        lock (_lock)
        {
            if (_serialPort?.IsOpen == true)
            {
                try { _serialPort.Close(); } catch { }
            }
            _serialPort?.Dispose();
            _serialPort = null;
        }

        ConnectionChanged?.Invoke(this, false);
    }

    public void SendCommand(string command)
    {
        lock (_lock)
        {
            if (_serialPort?.IsOpen == true)
            {
                _serialPort.WriteLine(command);
                RaiseDiag($"TX: {command}");
            }
        }
    }

    /// <summary>取得最近的原始資料 Hex Dump</summary>
    public string GetRawDataSnapshot()
    {
        lock (_diagLock)
        {
            return _rawBuffer.ToString();
        }
    }

    /// <summary>取得最近原始位元組的完整 hex dump (含地址+ASCII)</summary>
    public string GetHexDump()
    {
        byte[] bytes;
        lock (_diagLock) { bytes = _recentBytes.ToArray(); }

        if (bytes.Length == 0) return "(no data)";

        var sb = new StringBuilder();
        int show = Math.Min(bytes.Length, 512); // 顯示前 512 bytes
        for (int i = 0; i < show; i += 16)
        {
            sb.Append($"{i:X4}: ");
            // Hex part
            for (int j = 0; j < 16; j++)
            {
                if (i + j < show)
                    sb.Append($"{bytes[i + j]:X2} ");
                else
                    sb.Append("   ");
            }
            sb.Append(" | ");
            // ASCII part
            for (int j = 0; j < 16 && (i + j) < show; j++)
            {
                byte b = bytes[i + j];
                sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>將最近的原始位元組存到檔案，回傳檔案路徑</summary>
    public string SaveRawDataToFile()
    {
        byte[] bytes;
        lock (_diagLock) { bytes = _recentBytes.ToArray(); }

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TRANZX", "PressureData");
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, $"raw_serial_{DateTime.Now:yyyyMMdd_HHmmss}.bin");
        File.WriteAllBytes(path, bytes);

        // 也存一份 hex dump 文字檔
        var hexPath = Path.ChangeExtension(path, ".txt");
        File.WriteAllText(hexPath, GetHexDump());

        RaiseDiag($"已存檔: {path} ({bytes.Length} bytes)");
        return path;
    }

    /// <summary>啟動大量擷取模式 - 收集更多原始資料</summary>
    public void StartBulkCapture(int maxBytes = 65536)
    {
        lock (_diagLock)
        {
            _recentBytes = Array.Empty<byte>();
            _bulkCaptureMax = maxBytes;
            _bulkCaptureActive = true;
        }
        RaiseDiag($"開始大量擷取 (最多 {maxBytes} bytes)...");
    }

    private int _bulkCaptureMax = 4096;
    private bool _bulkCaptureActive;

    /// <summary>清除診斷緩衝區</summary>
    public void ClearDiagBuffer()
    {
        lock (_diagLock) { _rawBuffer.Clear(); _rawAscii.Clear(); _recentBytes = Array.Empty<byte>(); }
    }

    // ═══════════════════════════════════════════
    //  Read Loop - Binary Protocol
    // ═══════════════════════════════════════════
    //
    //  Protocol (from hex dump analysis):
    //    Packet: FF FE [counter:1] [data:100]  = 103 bytes
    //    counter % 40 = row index (0-39)
    //    data = 50 × uint16 little-endian values
    //    First 40 values = 40 columns of sensor data
    //    40 packets (rows 0-39) = 1 complete frame
    //
    // ═══════════════════════════════════════════

    private const int PacketSize = 103;      // FF FE + counter + 100 data bytes
    private const int ValuesPerRow = 50;     // 50 × uint16 LE per packet
    private const int UsableCols = 40;       // only first 40 columns used

    private async Task ReadLoop(CancellationToken ct)
    {
        // Ring buffer for incoming binary data
        var ringBuffer = new List<byte>(8192);
        long frameIndex = 0;
        var fpsStopwatch = System.Diagnostics.Stopwatch.StartNew();
        int fpsCounter = 0;

        // Current frame being assembled (rows 0-39)
        var frameData = new ushort[SensorConfig.Rows, ValuesPerRow];
        var rowReceived = new bool[SensorConfig.Rows];
        int lastRowIndex = -1;

        RaiseDiag("ReadLoop 開始 - Binary 模式 (FF FE sync, 103 bytes/packet)");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Read raw bytes from serial port
                byte[]? rawBytes = null;
                lock (_lock)
                {
                    if (_serialPort?.IsOpen != true) break;
                    int available = _serialPort.BytesToRead;
                    if (available > 0)
                    {
                        rawBytes = new byte[Math.Min(available, 4096)];
                        int read = _serialPort.Read(rawBytes, 0, rawBytes.Length);
                        if (read < rawBytes.Length) Array.Resize(ref rawBytes, read);
                        BytesReceived += read;
                    }
                }

                if (rawBytes == null)
                {
                    await Task.Delay(2, ct);
                    continue;
                }

                // Diagnostic: store raw bytes
                if (DiagnosticMode)
                {
                    lock (_diagLock)
                    {
                        int maxSize = _bulkCaptureActive ? _bulkCaptureMax : 4096;
                        var combined = new byte[_recentBytes.Length + rawBytes.Length];
                        _recentBytes.CopyTo(combined, 0);
                        rawBytes.CopyTo(combined, _recentBytes.Length);
                        if (combined.Length > maxSize)
                        {
                            _recentBytes = new byte[maxSize];
                            Array.Copy(combined, combined.Length - maxSize, _recentBytes, 0, maxSize);
                            if (_bulkCaptureActive) _bulkCaptureActive = false;
                        }
                        else _recentBytes = combined;

                        var hexLine = BitConverter.ToString(rawBytes, 0, Math.Min(rawBytes.Length, 64)).Replace("-", " ");
                        _rawBuffer.Insert(0, hexLine + "\n");
                        if (_rawBuffer.Length > MaxDiagBufferSize)
                            _rawBuffer.Remove(MaxDiagBufferSize / 2, _rawBuffer.Length - MaxDiagBufferSize / 2);
                    }
                    RawDataReceived?.Invoke(this, "");
                }

                // Add to ring buffer
                ringBuffer.AddRange(rawBytes);

                // Process complete packets from ring buffer
                while (ringBuffer.Count >= PacketSize)
                {
                    // Find sync header FF FE
                    int syncPos = FindSync(ringBuffer);
                    if (syncPos < 0)
                    {
                        // No sync found - discard all but last byte
                        ringBuffer.RemoveRange(0, ringBuffer.Count - 1);
                        break;
                    }

                    if (syncPos > 0)
                    {
                        // Discard bytes before sync
                        ringBuffer.RemoveRange(0, syncPos);
                    }

                    // Need full packet
                    if (ringBuffer.Count < PacketSize)
                        break;

                    // Extract packet
                    byte counter = ringBuffer[2];
                    int rowIndex = counter % SensorConfig.Rows; // 0-39

                    // Parse 50 × uint16 LE values from bytes 3..102
                    for (int v = 0; v < ValuesPerRow; v++)
                    {
                        int offset = 3 + v * 2;
                        ushort value = (ushort)(ringBuffer[offset] | (ringBuffer[offset + 1] << 8));
                        frameData[rowIndex, v] = value;
                    }
                    rowReceived[rowIndex] = true;

                    // Detect frame boundary: when row wraps from high to low
                    if (rowIndex < lastRowIndex || (rowIndex == SensorConfig.Rows - 1 && AllRowsReceived(rowReceived)))
                    {
                        // Emit frame if we have enough rows
                        int receivedCount = rowReceived.Count(r => r);
                        if (receivedCount >= SensorConfig.Rows - 2) // allow 1-2 missing rows
                        {
                            var frame = BuildFrameFromBinary(frameData, frameIndex++);
                            EmitFrame(frame, ref fpsCounter, fpsStopwatch);
                        }

                        // Reset for next frame
                        Array.Clear(frameData);
                        Array.Fill(rowReceived, false);

                        // Re-mark current row (it belongs to the new frame if rowIndex == 0)
                        if (rowIndex == 0)
                        {
                            for (int v = 0; v < ValuesPerRow; v++)
                            {
                                int offset = 3 + v * 2;
                                ushort value = (ushort)(ringBuffer[offset] | (ringBuffer[offset + 1] << 8));
                                frameData[0, v] = value;
                            }
                            rowReceived[0] = true;
                        }
                    }
                    lastRowIndex = rowIndex;

                    // Remove processed packet
                    ringBuffer.RemoveRange(0, PacketSize);
                }

                // Prevent ring buffer from growing unbounded
                if (ringBuffer.Count > 32768)
                    ringBuffer.RemoveRange(0, ringBuffer.Count - 8192);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                LastError = ex.Message;
                RaiseDiag($"讀取錯誤: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"讀取錯誤: {ex.Message}");
                await Task.Delay(100, ct);
            }
        }

        RaiseDiag("ReadLoop 結束");
    }

    private static int FindSync(List<byte> buffer)
    {
        for (int i = 0; i <= buffer.Count - 2; i++)
        {
            if (buffer[i] == 0xFF && buffer[i + 1] == 0xFE)
                return i;
        }
        return -1;
    }

    private static bool AllRowsReceived(bool[] rows)
    {
        foreach (var r in rows) if (!r) return false;
        return true;
    }

    /// <summary>
    /// Build a SensorFrame from parsed binary data.
    /// Raw ADC values are stored directly as force (grams) for now.
    /// Calibration can be refined later.
    /// </summary>
    private SensorFrame BuildFrameFromBinary(ushort[,] data, long frameIndex)
    {
        var frame = new SensorFrame
        {
            FrameIndex = frameIndex,
            TimestampMs = Environment.TickCount64,
            IsPreCalibrated = true  // 已在此處設定 PressureGrams，跳過 R→F 校正
        };

        // 固定 ADC 縮放因子：ADC 滿量程 = MaxForceGrams (1000g)
        // 不再使用自適應縮放（會將底噪放大成假壓力訊號）
        double scaleFactor = SensorConfig.MaxForceGrams / SensorConfig.AdcFullScale;

        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                ushort raw = data[r, c];
                frame.RawResistance[r, c] = raw;
                frame.PressureGrams[r, c] = Math.Clamp(raw * scaleFactor, 0, SensorConfig.MaxForceGrams);
            }
        }

        frame.ComputeStatistics();
        return frame;
    }

    private void EmitFrame(SensorFrame frame, ref int fpsCounter, System.Diagnostics.Stopwatch sw)
    {
        FramesReceived++;
        fpsCounter++;

        if (sw.ElapsedMilliseconds >= 1000)
        {
            CurrentFps = fpsCounter * 1000.0 / sw.ElapsedMilliseconds;
            fpsCounter = 0;
            sw.Restart();
        }

        FrameReceived?.Invoke(this, frame);
    }

    // ═══════════════════════════════════════════
    //  Parsers
    // ═══════════════════════════════════════════

    /// <summary>
    /// 解析 ASCII-FRAME 格式: "ROW:行號:值1,值2,...,值40"
    /// </summary>
    private SensorFrame? ParseAsciiFrame(List<string> lines, long frameIndex)
    {
        if (lines.Count != SensorConfig.Rows) return null;

        var frame = new SensorFrame
        {
            FrameIndex = frameIndex,
            TimestampMs = Environment.TickCount64
        };

        try
        {
            foreach (var line in lines)
            {
                var parts = line.Split(':');
                if (parts.Length >= 3 && parts[0] == "ROW")
                {
                    int row = int.Parse(parts[1]);
                    var values = parts[2].Split(',');
                    if (values.Length != SensorConfig.Cols) return null;

                    for (int c = 0; c < SensorConfig.Cols; c++)
                        frame.RawResistance[row, c] = double.Parse(values[c]);
                }
                else
                {
                    // 也嘗試直接逗號分隔
                    var values = line.Split(',');
                    if (values.Length == SensorConfig.Cols)
                    {
                        int row = lines.IndexOf(line);
                        for (int c = 0; c < SensorConfig.Cols; c++)
                            frame.RawResistance[row, c] = double.Parse(values[c]);
                    }
                    else return null;
                }
            }
            return frame;
        }
        catch { return null; }
    }

    /// <summary>
    /// 解析純 CSV 格式: 每行 40 個逗號分隔數值
    /// </summary>
    private SensorFrame? ParseCsvFrame(List<string> lines, long frameIndex)
    {
        if (lines.Count != SensorConfig.Rows) return null;

        var frame = new SensorFrame
        {
            FrameIndex = frameIndex,
            TimestampMs = Environment.TickCount64
        };

        try
        {
            for (int r = 0; r < lines.Count; r++)
            {
                var values = lines[r].Split(',');
                if (values.Length < SensorConfig.Cols) return null;

                for (int c = 0; c < SensorConfig.Cols; c++)
                    frame.RawResistance[r, c] = double.Parse(values[c].Trim());
            }
            return frame;
        }
        catch { return null; }
    }

    // ═══════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════

    private static bool IsCommaSeparatedNumbers(string line)
    {
        if (!line.Contains(',')) return false;
        var parts = line.Split(',');
        if (parts.Length < 5) return false;
        // 檢查前幾個是否為數字
        int numCount = 0;
        foreach (var p in parts.Take(5))
        {
            if (double.TryParse(p.Trim(), out _)) numCount++;
        }
        return numCount >= 4;
    }

    private void RaiseDiag(string msg)
    {
        DiagnosticMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
    }

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Serial 資料解析模式
/// </summary>
public enum SerialParseMode
{
    /// <summary>自動偵測格式</summary>
    AutoDetect,
    /// <summary>FRAME:/ROW:/END 格式</summary>
    AsciiFrame,
    /// <summary>純 CSV 逗號分隔 (每行40值, 連續40行)</summary>
    AsciiCsv,
    /// <summary>二進位模式 (每點2 bytes)</summary>
    RawBinary
}
