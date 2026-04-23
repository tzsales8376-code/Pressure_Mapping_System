using System.Diagnostics;
using PressureMappingSystem.Models;

namespace PressureMappingSystem.Services;

/// <summary>
/// 資料模擬器：在無硬體連接時產生模擬感測器資料
/// 用於軟體開發測試、Demo 展示、客戶培訓
/// 支援多種模擬模式：靜態壓力、動態移動、脈衝、隨機
/// </summary>
public class DataSimulatorService : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _simulationTask;
    private readonly Random _rng = new();
    private long _frameIndex;
    private readonly Stopwatch _stopwatch = new();

    public int TargetFps { get; set; } = SensorConfig.DefaultTargetFps;
    public SimulationMode Mode { get; set; } = SimulationMode.MovingPressure;
    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
    public double CurrentFps { get; private set; }

    // 模擬參數
    public double SimulatedMaxForceGrams { get; set; } = 500.0;
    public double NoiseLevel { get; set; } = 0.02; // 2% noise

    public event EventHandler<SensorFrame>? FrameGenerated;

    /// <summary>
    /// 啟動模擬
    /// </summary>
    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        _frameIndex = 0;
        _stopwatch.Restart();
        _simulationTask = Task.Run(() => SimulationLoop(_cts.Token));
    }

    /// <summary>
    /// 停止模擬
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _simulationTask?.Wait(TimeSpan.FromSeconds(2));
        _cts?.Dispose();
        _cts = null;
        _stopwatch.Stop();
    }

    private async Task SimulationLoop(CancellationToken ct)
    {
        double frameIntervalMs = 1000.0 / TargetFps;
        var timer = Stopwatch.StartNew();
        int fpsCount = 0;
        var fpsTimer = Stopwatch.StartNew();

        while (!ct.IsCancellationRequested)
        {
            var frame = GenerateFrame();
            FrameGenerated?.Invoke(this, frame);

            fpsCount++;
            if (fpsTimer.ElapsedMilliseconds >= 1000)
            {
                CurrentFps = fpsCount * 1000.0 / fpsTimer.ElapsedMilliseconds;
                fpsCount = 0;
                fpsTimer.Restart();
            }

            // 精確計時
            double elapsed = timer.Elapsed.TotalMilliseconds;
            double nextFrame = (_frameIndex) * frameIntervalMs;
            double sleepMs = nextFrame - elapsed;
            if (sleepMs > 1)
                await Task.Delay((int)sleepMs, ct);
        }
    }

    private SensorFrame GenerateFrame()
    {
        var frame = new SensorFrame
        {
            FrameIndex = _frameIndex++,
            TimestampMs = _stopwatch.Elapsed.TotalMilliseconds
        };

        double time = frame.TimestampMs / 1000.0; // seconds

        switch (Mode)
        {
            case SimulationMode.StaticCenter:
                GenerateStaticCenter(frame);
                break;
            case SimulationMode.MovingPressure:
                GenerateMovingPressure(frame, time);
                break;
            case SimulationMode.PulsingPressure:
                GeneratePulsingPressure(frame, time);
                break;
            case SimulationMode.MultiPoint:
                GenerateMultiPoint(frame, time);
                break;
            case SimulationMode.FullSurface:
                GenerateFullSurface(frame, time);
                break;
            case SimulationMode.RandomNoise:
                GenerateRandomNoise(frame);
                break;
        }

        return frame;
    }

    /// <summary>靜態中心壓力：模擬單點按壓</summary>
    private void GenerateStaticCenter(SensorFrame frame)
    {
        int cx = SensorConfig.Cols / 2;
        int cy = SensorConfig.Rows / 2;
        double radius = 6.0;

        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                double dist = Math.Sqrt((r - cy) * (r - cy) + (c - cx) * (c - cx));
                if (dist < radius)
                {
                    double force = SimulatedMaxForceGrams * (1.0 - dist / radius);
                    force += force * NoiseLevel * (_rng.NextDouble() * 2 - 1);
                    double resistance = ForceToSimulatedResistance(Math.Max(0, force));
                    frame.RawResistance[r, c] = resistance;
                }
                else
                {
                    frame.RawResistance[r, c] = SensorConfig.NoTouchResistanceKOhm +
                                                 _rng.NextDouble() * 100;
                }
            }
        }
    }

    /// <summary>移動壓力：模擬壓力區域隨時間移動</summary>
    private void GenerateMovingPressure(SensorFrame frame, double time)
    {
        double angle = time * 0.8; // 旋轉速度
        double radius = 10.0;
        double cx = SensorConfig.Cols / 2.0 + Math.Cos(angle) * 8;
        double cy = SensorConfig.Rows / 2.0 + Math.Sin(angle) * 8;

        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                double dist = Math.Sqrt((r - cy) * (r - cy) + (c - cx) * (c - cx));
                if (dist < radius)
                {
                    double force = SimulatedMaxForceGrams * Math.Pow(1.0 - dist / radius, 1.5);
                    force += force * NoiseLevel * (_rng.NextDouble() * 2 - 1);
                    frame.RawResistance[r, c] = ForceToSimulatedResistance(Math.Max(5, force));
                }
                else
                {
                    frame.RawResistance[r, c] = SensorConfig.NoTouchResistanceKOhm +
                                                 _rng.NextDouble() * 200;
                }
            }
        }
    }

    /// <summary>脈衝壓力：模擬週期性施加/釋放壓力</summary>
    private void GeneratePulsingPressure(SensorFrame frame, double time)
    {
        double pulse = (Math.Sin(time * 2 * Math.PI * 0.5) + 1) / 2; // 0.5Hz 脈衝
        double currentMax = SimulatedMaxForceGrams * pulse;

        int cx = SensorConfig.Cols / 2;
        int cy = SensorConfig.Rows / 2;

        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                double dist = Math.Sqrt((r - cy) * (r - cy) + (c - cx) * (c - cx));
                double radius = 8 + pulse * 4;
                if (dist < radius && currentMax > 5)
                {
                    double force = currentMax * (1.0 - dist / radius);
                    force += force * NoiseLevel * (_rng.NextDouble() * 2 - 1);
                    frame.RawResistance[r, c] = ForceToSimulatedResistance(Math.Max(0, force));
                }
                else
                {
                    frame.RawResistance[r, c] = SensorConfig.NoTouchResistanceKOhm +
                                                 _rng.NextDouble() * 100;
                }
            }
        }
    }

    /// <summary>多點壓力：模擬多個壓力點</summary>
    private void GenerateMultiPoint(SensorFrame frame, double time)
    {
        var points = new[]
        {
            (row: 10.0, col: 10.0, force: 300.0, radius: 5.0),
            (row: 10.0, col: 30.0, force: 600.0, radius: 7.0),
            (row: 30.0, col: 20.0, force: 400.0 + 200 * Math.Sin(time * 2), radius: 6.0),
        };

        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                double maxForce = 0;
                foreach (var p in points)
                {
                    double dist = Math.Sqrt((r - p.row) * (r - p.row) + (c - p.col) * (c - p.col));
                    if (dist < p.radius)
                    {
                        double f = p.force * (1.0 - dist / p.radius);
                        maxForce = Math.Max(maxForce, f);
                    }
                }

                if (maxForce >= 5)
                {
                    maxForce += maxForce * NoiseLevel * (_rng.NextDouble() * 2 - 1);
                    frame.RawResistance[r, c] = ForceToSimulatedResistance(maxForce);
                }
                else
                {
                    frame.RawResistance[r, c] = SensorConfig.NoTouchResistanceKOhm +
                                                 _rng.NextDouble() * 100;
                }
            }
        }
    }

    /// <summary>全面壓力：模擬均勻分佈壓力</summary>
    private void GenerateFullSurface(SensorFrame frame, double time)
    {
        double baseForce = SimulatedMaxForceGrams * 0.5;
        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                double variation = Math.Sin(r * 0.3 + time) * Math.Cos(c * 0.3 + time * 0.7);
                double force = baseForce + baseForce * 0.3 * variation;
                force += force * NoiseLevel * (_rng.NextDouble() * 2 - 1);
                frame.RawResistance[r, c] = ForceToSimulatedResistance(Math.Max(5, force));
            }
        }
    }

    /// <summary>隨機雜訊</summary>
    private void GenerateRandomNoise(SensorFrame frame)
    {
        for (int r = 0; r < SensorConfig.Rows; r++)
            for (int c = 0; c < SensorConfig.Cols; c++)
                frame.RawResistance[r, c] = SensorConfig.NoTouchResistanceKOhm +
                                             _rng.NextDouble() * 100;
    }

    /// <summary>
    /// 壓力→電阻模擬（規格書 R@F 曲線的反向近似）
    /// R(KΩ) ≈ A / (F + B) + C
    /// </summary>
    private double ForceToSimulatedResistance(double forceGrams)
    {
        if (forceGrams < SensorConfig.TriggerForceGrams)
            return SensorConfig.NoTouchResistanceKOhm + _rng.NextDouble() * 200;

        // 使用規格書曲線的反向模型
        // R@F 近似：R = 8000 / (F + 10) + 0.5  (KΩ, g)
        double resistance = 8000.0 / (forceGrams + 10.0) + 0.5;
        resistance = Math.Clamp(resistance, SensorConfig.MinResistanceKOhm,
                                SensorConfig.MaxResistanceKOhm);
        return resistance;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}

public enum SimulationMode
{
    StaticCenter,     // 靜態中心點
    MovingPressure,   // 移動壓力區
    PulsingPressure,  // 脈衝壓力
    MultiPoint,       // 多點壓力
    FullSurface,      // 全面壓力
    RandomNoise       // 隨機雜訊
}
