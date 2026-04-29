using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PressureMappingSystem.Models;
using SkiaSharp;

namespace PressureMappingSystem.Services;

/// <summary>
/// 匯出服務：將壓力數據匯出為 PNG, JPG, CSV, MP4 格式
/// </summary>
public class ExportService
{
    private readonly string _exportFolder;

    /// <summary>
    /// 色階上限 (g/point)，設為 0 時使用 SensorConfig.MaxForceGrams
    /// 由 ViewModel 在設定變更時同步更新
    /// </summary>
    public double ColorScaleMax { get; set; }

    public ExportService(string exportFolder)
    {
        _exportFolder = exportFolder;
        Directory.CreateDirectory(_exportFolder);
    }

    /// <summary>
    /// 匯出單幀為 CSV
    /// </summary>
    /// <summary>
    /// 匯出單幀為 CSV（支援完整路徑或僅檔名）
    /// </summary>
    public string ExportFrameToCsv(SensorFrame frame, string? filePathOrName = null)
    {
        filePathOrName ??= $"PressureData_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        // 如果是完整路徑就直接用，否則放到預設資料夾
        string path = Path.IsPathRooted(filePathOrName)
            ? filePathOrName
            : Path.Combine(_exportFolder, filePathOrName);

        var sb = new StringBuilder();

        // Header
        sb.AppendLine("# Pressure Mapping System - Data Export");
        sb.AppendLine($"# Timestamp: {frame.TimestampMs:F2} ms");
        sb.AppendLine($"# Peak Pressure: {frame.PeakPressureGrams:F1} g at ({frame.PeakRow},{frame.PeakCol})");
        sb.AppendLine($"# Total Force: {frame.TotalForceGrams:F1} g");
        sb.AppendLine($"# Active Points: {frame.ActivePointCount}");
        sb.AppendLine($"# Contact Area: {frame.ContactAreaMm2:F2} mm2");
        sb.AppendLine($"# Center of Pressure: ({frame.CenterOfPressureX:F2}, {frame.CenterOfPressureY:F2}) mm");
        sb.AppendLine();

        // Column headers
        sb.Append("Row\\Col");
        for (int c = 0; c < SensorConfig.Cols; c++)
            sb.Append($",{c + 1}");
        sb.AppendLine();

        // Data rows (pressure in grams)
        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            sb.Append(r + 1);
            for (int c = 0; c < SensorConfig.Cols; c++)
                sb.Append($",{frame.PressureGrams[r, c]:F1}");
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
        return path;
    }

    /// <summary>
    /// 匯出錄製會話為多個 CSV (時間序列)
    /// </summary>
    public string ExportSessionToCsv(RecordingSession session, string? folderName = null)
    {
        folderName ??= $"Session_{session.SessionId}";
        string folder = Path.Combine(_exportFolder, folderName);
        Directory.CreateDirectory(folder);

        // 匯出各幀
        for (int i = 0; i < session.Frames.Count; i++)
        {
            ExportFrameToCsvInternal(session.Frames[i],
                Path.Combine(folder, $"frame_{i:D6}.csv"));
        }

        // 匯出 Peak Map
        ExportPeakMapCsv(session.PeakPressureMap,
            Path.Combine(folder, "peak_pressure_map.csv"));

        // 匯出摘要
        ExportSessionSummary(session, Path.Combine(folder, "summary.csv"));

        return folder;
    }

    /// <summary>
    /// 匯出熱力圖為 PNG/JPG
    /// </summary>
    /// <summary>
    /// 匯出熱力圖為 PNG/JPG（支援完整路徑或僅檔名）
    /// </summary>
    public string ExportHeatmapImage(SensorFrame frame, string format = "png",
        int imageWidth = 800, int imageHeight = 800, string? filename = null)
    {
        string ext = format.ToLower() == "jpg" ? "jpg" : "png";
        filename ??= $"Heatmap_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";
        string path = Path.IsPathRooted(filename)
            ? filename
            : Path.Combine(_exportFolder, filename);

        int legendWidth = 80;
        int totalWidth = imageWidth + legendWidth + 20;

        using var surface = SKSurface.Create(new SKImageInfo(totalWidth, imageHeight));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        // 繪製熱力圖
        RenderHeatmapToCanvas(canvas, frame.PressureGrams, 0, 0, imageWidth, imageHeight, ColorScaleMax);

        // 繪製顏色圖例
        RenderColorLegend(canvas, imageWidth + 10, 40, legendWidth - 20, imageHeight - 80, ColorScaleMax);

        // 標題
        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 16,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold,
                SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        };
        canvas.DrawText($"Peak: {frame.PeakPressureGrams:F0}g | " +
                        $"Total: {frame.TotalForceGrams:F0}g", 10, 20, titlePaint);

        using var image = surface.Snapshot();
        using var data = ext == "jpg"
            ? image.Encode(SKEncodedImageFormat.Jpeg, 95)
            : image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);

        return path;
    }

    /// <summary>
    /// 匯出錄製會話為 AVI 影片（純 C# MJPEG，不需要 FFmpeg）
    /// 輸出標準 RIFF AVI 格式，Windows Media Player / VLC 等皆可播放
    /// </summary>
    public Task<string> ExportSessionToMp4(RecordingSession session, int fps = 30,
        int width = 800, int height = 800, string? filename = null,
        IProgress<double>? progress = null)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        filename ??= $"Recording_{stamp}.avi";
        string outputPath = Path.IsPathRooted(filename)
            ? filename
            : Path.Combine(_exportFolder, filename);

        int legendWidth = 80;
        int totalWidth = width + legendWidth + 20;
        if (totalWidth % 2 != 0) totalWidth++;
        if (height % 2 != 0) height++;

        int frameCount = session.Frames.Count;
        if (frameCount == 0)
            throw new InvalidOperationException("沒有可匯出的幀");

        // 根據錄製實際時長計算真實 FPS，確保影片長度與錄製時間一致
        double durationSec = session.DurationMs / 1000.0;
        if (durationSec > 0.1 && frameCount > 1)
            fps = Math.Max(1, (int)Math.Round(frameCount / durationSec));

        // 渲染所有幀為 JPEG
        var jpegFrames = new List<byte[]>(frameCount);
        for (int i = 0; i < frameCount; i++)
        {
            var frame = session.Frames[i];
            using var surface = SKSurface.Create(new SKImageInfo(totalWidth, height));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);

            RenderHeatmapToCanvas(canvas, frame.PressureGrams, 0, 0, width, height, ColorScaleMax);
            RenderColorLegend(canvas, width + 10, 40, legendWidth - 20, height - 80, ColorScaleMax);

            using var infoPaint = new SKPaint
            {
                Color = SKColors.Black,
                TextSize = 14,
                IsAntialias = true
            };
            canvas.DrawText(
                $"Frame {i + 1}/{frameCount} | " +
                $"Time: {frame.TimestampMs / 1000:F2}s | " +
                $"Peak: {frame.PeakPressureGrams:F0}g | " +
                $"Total: {frame.TotalForceGrams:F0}g",
                10, 20, infoPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
            jpegFrames.Add(data.ToArray());

            progress?.Report((double)(i + 1) / frameCount * 0.9);
        }

        // 寫入 MJPEG AVI
        WriteMjpegAvi(outputPath, jpegFrames, totalWidth, height, fps);
        progress?.Report(1.0);

        return Task.FromResult(outputPath);
    }

    /// <summary>
    /// 純 C# MJPEG AVI 寫入器
    /// 產生標準 RIFF AVI 格式，所有主流播放器皆可播放
    /// </summary>
    private static void WriteMjpegAvi(string path, List<byte[]> jpegFrames,
        int width, int height, int fps)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        int frameCount = jpegFrames.Count;

        // 計算各 chunk 大小
        var frameSizes = new List<int>(frameCount);
        long moviDataSize = 4; // 'movi' fourcc
        foreach (var jpeg in jpegFrames)
        {
            int paddedSize = jpeg.Length + (jpeg.Length % 2);
            frameSizes.Add(jpeg.Length);
            moviDataSize += 8 + paddedSize;
        }

        long idx1Size = 16L * frameCount;
        int avihSize = 56;
        int strhSize = 56;
        int strfSize = 40;
        int strlListSize = 4 + (8 + strhSize) + (8 + strfSize);
        int hdrlListSize = 4 + (8 + avihSize) + (8 + strlListSize);
        long riffSize = 4 + (8 + hdrlListSize) + (8 + moviDataSize) + (8 + idx1Size);

        // ── RIFF Header ──
        WriteFourCC(bw, "RIFF");
        bw.Write((uint)riffSize);
        WriteFourCC(bw, "AVI ");

        // ── hdrl LIST ──
        WriteFourCC(bw, "LIST");
        bw.Write((uint)hdrlListSize);
        WriteFourCC(bw, "hdrl");

        // ── avih ──
        WriteFourCC(bw, "avih");
        bw.Write((uint)avihSize);
        bw.Write((uint)(1000000 / fps));
        bw.Write((uint)0);
        bw.Write((uint)0);
        bw.Write((uint)0x10);          // AVIF_HASINDEX
        bw.Write((uint)frameCount);
        bw.Write((uint)0);
        bw.Write((uint)1);             // 1 stream
        bw.Write((uint)0);
        bw.Write((uint)width);
        bw.Write((uint)height);
        bw.Write((uint)0); bw.Write((uint)0);
        bw.Write((uint)0); bw.Write((uint)0);

        // ── strl LIST ──
        WriteFourCC(bw, "LIST");
        bw.Write((uint)strlListSize);
        WriteFourCC(bw, "strl");

        // ── strh ──
        WriteFourCC(bw, "strh");
        bw.Write((uint)strhSize);
        WriteFourCC(bw, "vids");
        WriteFourCC(bw, "MJPG");
        bw.Write((uint)0);
        bw.Write((ushort)0); bw.Write((ushort)0);
        bw.Write((uint)0);
        bw.Write((uint)1);             // dwScale
        bw.Write((uint)fps);           // dwRate
        bw.Write((uint)0);
        bw.Write((uint)frameCount);
        bw.Write((uint)0);
        bw.Write((uint)0xFFFFFFFF);    // dwQuality
        bw.Write((uint)0);
        bw.Write((ushort)0); bw.Write((ushort)0);
        bw.Write((ushort)width); bw.Write((ushort)height);

        // ── strf (BITMAPINFOHEADER) ──
        WriteFourCC(bw, "strf");
        bw.Write((uint)strfSize);
        bw.Write((uint)40);
        bw.Write((int)width);
        bw.Write((int)height);
        bw.Write((ushort)1);           // biPlanes
        bw.Write((ushort)24);          // biBitCount
        WriteFourCC(bw, "MJPG");       // biCompression
        bw.Write((uint)(width * height * 3));
        bw.Write((uint)0); bw.Write((uint)0);
        bw.Write((uint)0); bw.Write((uint)0);

        // ── movi LIST ──
        WriteFourCC(bw, "LIST");
        bw.Write((uint)moviDataSize);
        WriteFourCC(bw, "movi");

        var frameOffsets = new List<uint>(frameCount);
        uint moviOffset = 4;

        for (int i = 0; i < frameCount; i++)
        {
            var jpeg = jpegFrames[i];
            int paddedSize = jpeg.Length + (jpeg.Length % 2);
            frameOffsets.Add(moviOffset);

            WriteFourCC(bw, "00dc");
            bw.Write((uint)jpeg.Length);
            bw.Write(jpeg);
            if (jpeg.Length % 2 != 0)
                bw.Write((byte)0);

            moviOffset += (uint)(8 + paddedSize);
        }

        // ── idx1 ──
        WriteFourCC(bw, "idx1");
        bw.Write((uint)idx1Size);
        for (int i = 0; i < frameCount; i++)
        {
            WriteFourCC(bw, "00dc");
            bw.Write((uint)0x10);               // AVIIF_KEYFRAME
            bw.Write(frameOffsets[i]);
            bw.Write((uint)frameSizes[i]);
        }
    }

    private static void WriteFourCC(BinaryWriter bw, string fourcc)
    {
        bw.Write((byte)fourcc[0]);
        bw.Write((byte)fourcc[1]);
        bw.Write((byte)fourcc[2]);
        bw.Write((byte)fourcc[3]);
    }

    /// <summary>
    /// 將熱力圖渲染到 SkiaSharp Canvas
    /// </summary>
    public void RenderHeatmapToCanvas(SKCanvas canvas, double[,] data,
        float x, float y, float width, float height, double colorScaleMax = 0)
    {
        float cellW = width / SensorConfig.Cols;
        float cellH = height / SensorConfig.Rows;

        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                double pressure = data[r, c];
                var color = PressureToColor(pressure, colorScaleMax);

                using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
                canvas.DrawRect(x + c * cellW, y + r * cellH, cellW, cellH, paint);
            }
        }
    }

    /// <summary>
    /// 渲染顏色圖例 (Color Legend Bar)
    /// 顯示壓力值與顏色的對應關係
    /// </summary>
    public void RenderColorLegend(SKCanvas canvas, float x, float y, float width, float height, double colorScaleMax = 0)
    {
        double scaleMax = colorScaleMax > 0 ? colorScaleMax : SensorConfig.MaxForceGrams;
        int steps = 256;
        float stepH = height / steps;

        // 繪製漸層色條
        for (int i = 0; i < steps; i++)
        {
            double pressure = scaleMax * (1.0 - (double)i / steps);
            var color = PressureToColor(pressure, scaleMax);
            using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
            canvas.DrawRect(x, y + i * stepH, width * 0.6f, stepH + 1, paint);
        }

        // 繪製外框
        using var borderPaint = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1
        };
        canvas.DrawRect(x, y, width * 0.6f, height, borderPaint);

        // 繪製刻度標籤（自動間距）
        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 11,
            IsAntialias = true
        };

        double tickStep = CalculateNiceTickStep(scaleMax, 10);
        for (double tick = 0; tick <= scaleMax + 0.01; tick += tickStep)
        {
            float ty = y + (float)((scaleMax - tick) / scaleMax * height);
            canvas.DrawLine(x + width * 0.6f, ty, x + width * 0.7f, ty, borderPaint);
            canvas.DrawText($"{tick:F0}g", x + width * 0.72f, ty + 4, textPaint);
        }

        // 標題
        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 12,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold,
                SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        };
        canvas.DrawText("Pressure", x, y - 8, titlePaint);
        canvas.DrawText("(g)", x, y - 8 + 14, titlePaint);
    }

    private static double CalculateNiceTickStep(double range, int targetTicks)
    {
        if (range <= 0) return 1;
        double rough = range / targetTicks;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double normalized = rough / mag;
        double nice;
        if (normalized <= 1.5) nice = 1;
        else if (normalized <= 3) nice = 2;
        else if (normalized <= 7) nice = 5;
        else nice = 10;
        return nice * mag;
    }

    /// <summary>
    /// 壓力值 → 顏色映射 (256 階 Jet colormap)
    /// 統一使用 HeatmapControl.PressureToColor256
    /// 0g=白色(無壓力), 低→藍→青→綠→黃→紅
    /// </summary>
    public static SKColor PressureToColor(double pressureGrams, double maxGrams = 0)
    {
        return Controls.HeatmapControl.PressureToColor256(pressureGrams, maxGrams);
    }

    // ──── Internal helpers ────

    private void ExportFrameToCsvInternal(SensorFrame frame, string path)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append(frame.PressureGrams[r, c].ToString("F1"));
            }
            sb.AppendLine();
        }
        File.WriteAllText(path, sb.ToString());
    }

    private void ExportPeakMapCsv(double[,] peakMap, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Peak Pressure Map (maximum pressure per point across all frames)");
        for (int r = 0; r < SensorConfig.Rows; r++)
        {
            for (int c = 0; c < SensorConfig.Cols; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append(peakMap[r, c].ToString("F1"));
            }
            sb.AppendLine();
        }
        File.WriteAllText(path, sb.ToString());
    }

    private void ExportSessionSummary(RecordingSession session, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Frame,TimestampMs,PeakPressureG,TotalForceG,ActivePoints,ContactAreaMm2,CoPX,CoPY");
        foreach (var frame in session.Frames)
        {
            sb.AppendLine($"{frame.FrameIndex},{frame.TimestampMs:F2},{frame.PeakPressureGrams:F1}," +
                         $"{frame.TotalForceGrams:F1},{frame.ActivePointCount}," +
                         $"{frame.ContactAreaMm2:F2},{frame.CenterOfPressureX:F2},{frame.CenterOfPressureY:F2}");
        }
        File.WriteAllText(path, sb.ToString());
    }

    // FindFfmpeg 和 ExportSessionToGif 已移除 — 改用純 C# MJPEG AVI 寫入器
}
