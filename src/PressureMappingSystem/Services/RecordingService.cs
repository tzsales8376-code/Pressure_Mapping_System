using System.IO;
using PressureMappingSystem.Models;

namespace PressureMappingSystem.Services;

/// <summary>
/// 錄製服務：管理壓力資料的錄製、儲存與回放
/// 支援手動控制、壓力觸發、定時錄製三種模式
/// </summary>
public class RecordingService
{
    private RecordingSession? _currentSession;
    private readonly List<RecordingSession> _savedSessions = new();
    private readonly string _recordingsFolder;
    private readonly object _lock = new();

    // 回放狀態
    private int _playbackIndex;
    private CancellationTokenSource? _playbackCts;
    private Task? _playbackTask;

    public RecordingSession? CurrentSession => _currentSession;
    public IReadOnlyList<RecordingSession> SavedSessions => _savedSessions;
    public bool IsRecording => _currentSession?.IsRecording ?? false;
    public bool IsPlaying => _playbackTask != null && !_playbackTask.IsCompleted;

    public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;
    public int PlaybackIndex => _playbackIndex;
    public double PlaybackSpeed { get; set; } = 1.0;

    // 事件
    public event EventHandler<RecordingSession>? RecordingStarted;
    public event EventHandler<RecordingSession>? RecordingStopped;
    public event EventHandler<SensorFrame>? PlaybackFrameChanged;
    public event EventHandler<PlaybackState>? PlaybackStateChanged;

    public RecordingService(string recordingsFolder)
    {
        _recordingsFolder = recordingsFolder;
        Directory.CreateDirectory(_recordingsFolder);
    }

    /// <summary>
    /// 開始錄製
    /// </summary>
    public RecordingSession StartRecording(string? name = null, TriggerCondition? trigger = null,
        int targetFps = 60, string calibrationRecipeId = "")
    {
        StopRecording();

        _currentSession = new RecordingSession
        {
            Name = name ?? $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}",
            StartTime = DateTime.Now,
            IsRecording = true,
            TargetFps = targetFps,
            Trigger = trigger,
            CalibrationRecipeId = calibrationRecipeId
        };

        RecordingStarted?.Invoke(this, _currentSession);
        return _currentSession;
    }

    /// <summary>
    /// 新增一幀到當前錄製
    /// </summary>
    public void AddFrame(SensorFrame frame)
    {
        lock (_lock)
        {
            if (_currentSession?.IsRecording != true) return;

            var trigger = _currentSession.Trigger;

            // 檢查觸發條件
            if (trigger?.Type == TriggerType.PressureThreshold && _currentSession.Frames.Count == 0)
            {
                if (frame.ActivePointCount < trigger.MinActivePoints ||
                    frame.PeakPressureGrams < trigger.ThresholdGrams)
                    return; // 尚未觸發
            }

            _currentSession.AddFrame(frame.DeepClone());

            // 檢查定時結束
            if (trigger?.Type == TriggerType.Timed &&
                _currentSession.DurationMs >= trigger.TimedDurationSeconds * 1000)
            {
                StopRecording();
            }

            // 檢查觸發後幀數限制
            if (trigger?.Type == TriggerType.PressureThreshold &&
                trigger.PostTriggerFrames > 0 &&
                _currentSession.Frames.Count >= trigger.PostTriggerFrames + trigger.PreTriggerFrames)
            {
                StopRecording();
            }
        }
    }

    /// <summary>
    /// 停止錄製
    /// </summary>
    public RecordingSession? StopRecording()
    {
        lock (_lock)
        {
            if (_currentSession == null) return null;

            _currentSession.IsRecording = false;
            _currentSession.EndTime = DateTime.Now;
            _currentSession.RecomputePeakMap();

            var session = _currentSession;
            _savedSessions.Add(session);
            RecordingStopped?.Invoke(this, session);

            _currentSession = null;
            return session;
        }
    }

    // ──── 回放控制 ────

    /// <summary>正向播放</summary>
    public void PlayForward(RecordingSession session)
    {
        StopPlayback();
        PlaybackState = PlaybackState.PlayingForward;
        PlaybackStateChanged?.Invoke(this, PlaybackState);
        _playbackCts = new CancellationTokenSource();
        _playbackTask = Task.Run(() => PlaybackLoop(session, 1, _playbackCts.Token));
    }

    /// <summary>反向播放</summary>
    public void PlayBackward(RecordingSession session)
    {
        StopPlayback();
        PlaybackState = PlaybackState.PlayingBackward;
        PlaybackStateChanged?.Invoke(this, PlaybackState);
        _playbackCts = new CancellationTokenSource();
        _playbackTask = Task.Run(() => PlaybackLoop(session, -1, _playbackCts.Token));
    }

    /// <summary>暫停播放</summary>
    public void PausePlayback()
    {
        _playbackCts?.Cancel();
        PlaybackState = PlaybackState.Paused;
        PlaybackStateChanged?.Invoke(this, PlaybackState);
    }

    /// <summary>停止播放</summary>
    public void StopPlayback()
    {
        _playbackCts?.Cancel();
        _playbackTask?.Wait(TimeSpan.FromSeconds(2));
        _playbackCts?.Dispose();
        _playbackCts = null;
        _playbackIndex = 0;
        PlaybackState = PlaybackState.Stopped;
        PlaybackStateChanged?.Invoke(this, PlaybackState);
    }

    /// <summary>單幀前進</summary>
    public void StepForward(RecordingSession session)
    {
        PausePlayback();
        if (_playbackIndex < session.Frames.Count - 1)
        {
            _playbackIndex++;
            PlaybackFrameChanged?.Invoke(this, session.Frames[_playbackIndex]);
        }
    }

    /// <summary>單幀後退</summary>
    public void StepBackward(RecordingSession session)
    {
        PausePlayback();
        if (_playbackIndex > 0)
        {
            _playbackIndex--;
            PlaybackFrameChanged?.Invoke(this, session.Frames[_playbackIndex]);
        }
    }

    /// <summary>跳轉到指定幀</summary>
    public void SeekTo(RecordingSession session, int frameIndex)
    {
        _playbackIndex = Math.Clamp(frameIndex, 0, session.Frames.Count - 1);
        PlaybackFrameChanged?.Invoke(this, session.Frames[_playbackIndex]);
    }

    private async Task PlaybackLoop(RecordingSession session, int direction, CancellationToken ct)
    {
        if (session.Frames.Count == 0) return;

        double frameIntervalMs = session.TargetFps > 0 ? 1000.0 / session.TargetFps : 16.67;

        while (!ct.IsCancellationRequested)
        {
            if (_playbackIndex >= 0 && _playbackIndex < session.Frames.Count)
            {
                PlaybackFrameChanged?.Invoke(this, session.Frames[_playbackIndex]);
            }

            _playbackIndex += direction;

            // 邊界處理
            if (_playbackIndex >= session.Frames.Count)
            {
                _playbackIndex = session.Frames.Count - 1;
                PlaybackState = PlaybackState.Stopped;
                PlaybackStateChanged?.Invoke(this, PlaybackState);
                break;
            }
            if (_playbackIndex < 0)
            {
                _playbackIndex = 0;
                PlaybackState = PlaybackState.Stopped;
                PlaybackStateChanged?.Invoke(this, PlaybackState);
                break;
            }

            int delayMs = (int)(frameIntervalMs / PlaybackSpeed);
            await Task.Delay(Math.Max(1, delayMs), ct);
        }
    }

    /// <summary>
    /// 儲存錄製會話到磁碟
    /// </summary>
    public void SaveSession(RecordingSession session)
    {
        string path = Path.Combine(_recordingsFolder, $"{session.SessionId}.dat");
        using var writer = new BinaryWriter(File.Create(path));

        // Header
        writer.Write("PMSREC");    // Magic
        writer.Write(1);            // Version
        writer.Write(session.SessionId);
        writer.Write(session.Name);
        writer.Write(session.StartTime.ToBinary());
        writer.Write(session.EndTime?.ToBinary() ?? 0L);
        writer.Write(session.TargetFps);
        writer.Write(session.CalibrationRecipeId);
        writer.Write(session.Frames.Count);

        // Frames
        foreach (var frame in session.Frames)
        {
            writer.Write(frame.FrameIndex);
            writer.Write(frame.TimestampMs);
            for (int r = 0; r < SensorConfig.Rows; r++)
                for (int c = 0; c < SensorConfig.Cols; c++)
                    writer.Write(frame.RawResistance[r, c]);
            for (int r = 0; r < SensorConfig.Rows; r++)
                for (int c = 0; c < SensorConfig.Cols; c++)
                    writer.Write(frame.PressureGrams[r, c]);
        }
    }

    /// <summary>
    /// 從磁碟載入錄製會話
    /// </summary>
    public RecordingSession? LoadSession(string filePath)
    {
        try
        {
            using var reader = new BinaryReader(File.OpenRead(filePath));
            string magic = reader.ReadString();
            if (magic != "PMSREC") return null;

            int version = reader.ReadInt32();
            var session = new RecordingSession
            {
                SessionId = reader.ReadString(),
                Name = reader.ReadString(),
                StartTime = DateTime.FromBinary(reader.ReadInt64()),
                TargetFps = 0
            };
            long endTimeBin = reader.ReadInt64();
            if (endTimeBin != 0) session.EndTime = DateTime.FromBinary(endTimeBin);
            session.TargetFps = reader.ReadInt32();
            session.CalibrationRecipeId = reader.ReadString();
            int frameCount = reader.ReadInt32();

            for (int f = 0; f < frameCount; f++)
            {
                var frame = new SensorFrame
                {
                    FrameIndex = reader.ReadInt64(),
                    TimestampMs = reader.ReadDouble()
                };
                for (int r = 0; r < SensorConfig.Rows; r++)
                    for (int c = 0; c < SensorConfig.Cols; c++)
                        frame.RawResistance[r, c] = reader.ReadDouble();
                for (int r = 0; r < SensorConfig.Rows; r++)
                    for (int c = 0; c < SensorConfig.Cols; c++)
                        frame.PressureGrams[r, c] = reader.ReadDouble();
                frame.ComputeStatistics();
                session.AddFrame(frame);
            }

            _savedSessions.Add(session);
            return session;
        }
        catch
        {
            return null;
        }
    }
}

public enum PlaybackState
{
    Stopped,
    PlayingForward,
    PlayingBackward,
    Paused
}
