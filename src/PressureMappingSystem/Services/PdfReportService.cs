using System.IO;
using PressureMappingSystem.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace PressureMappingSystem.Services;

/// <summary>
/// PDF 量測報告自動生成服務
/// 使用 QuestPDF 產生專業的校正報告與量測報告
/// 包含：TRANZX Logo、熱力圖、校正曲線、統計資料、峰值分析
/// </summary>
public class PdfReportService
{
    private readonly ExportService _exportService;
    private readonly string _outputFolder;
    private readonly byte[]? _logoBytes;

    public PdfReportService(ExportService exportService, string outputFolder, string? logoPath = null)
    {
        _exportService = exportService;
        _outputFolder = outputFolder;
        Directory.CreateDirectory(_outputFolder);

        // 設定 QuestPDF License
        QuestPDF.Settings.License = LicenseType.Community;

        if (logoPath != null && File.Exists(logoPath))
            _logoBytes = File.ReadAllBytes(logoPath);
    }

    /// <summary>
    /// 生成單幀量測報告
    /// </summary>
    public string GenerateMeasurementReport(SensorFrame frame, CalibrationRecipe recipe,
        string operatorName = "", string notes = "")
    {
        string filename = $"MeasurementReport_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        string path = Path.Combine(_outputFolder, filename);

        var L = LocalizationService.Instance;

        // 生成熱力圖影像
        byte[] heatmapBytes = GenerateHeatmapBytes(frame, 600, 600);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                // ── Header ──
                page.Header().Element(c => ComposeHeader(c, L.Get("Report.Title")));

                // ── Content ──
                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    // 報告資訊
                    col.Item().Element(c => ComposeReportInfo(c, recipe, operatorName, L));

                    // 量測摘要
                    col.Item().Element(c => ComposeMeasurementSummary(c, frame, L));

                    // 熱力圖
                    col.Item().Element(c => ComposeHeatmap(c, heatmapBytes, L));

                    // 校正資訊
                    col.Item().Element(c => ComposeCalibrationInfo(c, recipe, L));

                    // 備註
                    if (!string.IsNullOrEmpty(notes))
                    {
                        col.Item().PaddingTop(10).Column(noteCol =>
                        {
                            noteCol.Item().Text("Notes / 備註").Bold().FontSize(12);
                            noteCol.Item().PaddingTop(4).Text(notes).FontSize(9);
                        });
                    }
                });

                // ── Footer ──
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(path);

        return path;
    }

    /// <summary>
    /// 生成校正報告（客戶回廠校正用）
    /// </summary>
    public string GenerateCalibrationReport(CalibrationRecipe recipe,
        List<(double knownForce, SensorFrame measuredFrame)>? verificationData = null,
        string operatorName = "")
    {
        string filename = $"CalibrationReport_{recipe.RecipeId}_{DateTime.Now:yyyyMMdd}.pdf";
        string path = Path.Combine(_outputFolder, filename);
        var L = LocalizationService.Instance;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(c => ComposeHeader(c, $"{L.Get("Report.Calibration")} - {recipe.Name}"));

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    // 校正配方詳情
                    col.Item().Column(c2 =>
                    {
                        c2.Item().Text($"{L.Get("Report.CalRecipe")} {recipe.Name}").Bold().FontSize(14);
                        c2.Spacing(4);

                        c2.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cd =>
                            {
                                cd.RelativeColumn(1);
                                cd.RelativeColumn(2);
                            });

                            AddInfoRow(table, "Recipe ID", recipe.RecipeId);
                            AddInfoRow(table, L.Get("Report.Sensor"), recipe.SensorSerialNumber);
                            AddInfoRow(table, L.Get("Report.Operator"), recipe.CalibratedBy);
                            AddInfoRow(table, "Calibration Date", recipe.LastCalibratedDate.ToString("yyyy-MM-dd HH:mm"));
                            AddInfoRow(table, "Temperature", $"{recipe.CalibrationTemperature}°C");
                            AddInfoRow(table, "Interpolation", recipe.Interpolation.ToString());
                            AddInfoRow(table, "Lookup Points", recipe.LookupTable.Count.ToString());
                        });
                    });

                    // 校正查找表
                    col.Item().Column(c2 =>
                    {
                        c2.Item().PaddingTop(10).Text("Calibration Lookup Table / 校正查找表").Bold().FontSize(12);
                        c2.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(cd =>
                            {
                                cd.ConstantColumn(40);
                                cd.RelativeColumn(1);
                                cd.RelativeColumn(1);
                                cd.RelativeColumn(1);
                            });

                            // Header
                            table.Header(h =>
                            {
                                h.Cell().Background("#2B5797").Padding(4).Text("#").FontColor(Colors.White).Bold();
                                h.Cell().Background("#2B5797").Padding(4).Text("Resistance (KΩ)").FontColor(Colors.White).Bold();
                                h.Cell().Background("#2B5797").Padding(4).Text("Force (g)").FontColor(Colors.White).Bold();
                                h.Cell().Background("#2B5797").Padding(4).Text("Uncertainty (%)").FontColor(Colors.White).Bold();
                            });

                            for (int i = 0; i < recipe.LookupTable.Count; i++)
                            {
                                var pt = recipe.LookupTable[i];
                                string bg = i % 2 == 0 ? "#F5F5F5" : "#FFFFFF";
                                table.Cell().Background(bg).Padding(3).Text($"{i + 1}");
                                table.Cell().Background(bg).Padding(3).Text($"{pt.ResistanceKOhm:F2}");
                                table.Cell().Background(bg).Padding(3).Text($"{pt.ForceGrams:F1}");
                                table.Cell().Background(bg).Padding(3).Text($"±{pt.UncertaintyPercent:F1}%");
                            }
                        });
                    });

                    // 驗證數據
                    if (verificationData != null && verificationData.Count > 0)
                    {
                        col.Item().Column(c2 =>
                        {
                            c2.Item().PaddingTop(10).Text("Verification Data / 驗證數據").Bold().FontSize(12);
                            c2.Item().PaddingTop(4).Table(table =>
                            {
                                table.ColumnsDefinition(cd =>
                                {
                                    cd.RelativeColumn(1);
                                    cd.RelativeColumn(1);
                                    cd.RelativeColumn(1);
                                    cd.RelativeColumn(1);
                                });

                                table.Header(h =>
                                {
                                    h.Cell().Background("#ED7D31").Padding(4).Text("Known Force (g)").FontColor(Colors.White).Bold();
                                    h.Cell().Background("#ED7D31").Padding(4).Text("Measured Avg (g)").FontColor(Colors.White).Bold();
                                    h.Cell().Background("#ED7D31").Padding(4).Text("Error (%)").FontColor(Colors.White).Bold();
                                    h.Cell().Background("#ED7D31").Padding(4).Text("Status").FontColor(Colors.White).Bold();
                                });

                                foreach (var (knownForce, measuredFrame) in verificationData)
                                {
                                    double error = Math.Abs(measuredFrame.AveragePressureGrams - knownForce) / knownForce * 100;
                                    string status = error <= 5 ? "PASS" : error <= 10 ? "WARN" : "FAIL";
                                    string bg = status == "PASS" ? "#E8F5E9" : status == "WARN" ? "#FFF3E0" : "#FFEBEE";

                                    table.Cell().Background(bg).Padding(3).Text($"{knownForce:F1}");
                                    table.Cell().Background(bg).Padding(3).Text($"{measuredFrame.AveragePressureGrams:F1}");
                                    table.Cell().Background(bg).Padding(3).Text($"{error:F2}%");
                                    table.Cell().Background(bg).Padding(3).Text(status).Bold();
                                }
                            });
                        });
                    }
                });

                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(path);

        return path;
    }

    /// <summary>
    /// 生成錄製會話完整報告
    /// </summary>
    public string GenerateSessionReport(RecordingSession session, CalibrationRecipe recipe,
        string operatorName = "")
    {
        string filename = $"SessionReport_{session.SessionId}.pdf";
        string path = Path.Combine(_outputFolder, filename);
        var L = LocalizationService.Instance;

        // 生成 Peak 熱力圖
        var peakFrame = session.GlobalPeakFrame ?? (session.Frames.Count > 0 ? session.Frames[0] : null);
        byte[]? peakHeatmapBytes = peakFrame != null ? GenerateHeatmapBytes(peakFrame, 500, 500) : null;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(c => ComposeHeader(c, $"Session Report - {session.Name}"));

                page.Content().Column(col =>
                {
                    col.Spacing(8);

                    // Session info
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cd => { cd.RelativeColumn(1); cd.RelativeColumn(2); });
                        AddInfoRow(table, "Session ID", session.SessionId);
                        AddInfoRow(table, "Start Time", session.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
                        AddInfoRow(table, "Duration", $"{session.DurationMs / 1000:F2} seconds");
                        AddInfoRow(table, "Total Frames", session.Frames.Count.ToString());
                        AddInfoRow(table, "Actual FPS", $"{session.ActualFps:F1}");
                        AddInfoRow(table, L.Get("Report.CalRecipe"), session.CalibrationRecipeId);
                        AddInfoRow(table, L.Get("Report.Operator"), operatorName);
                    });

                    // Peak Summary
                    if (peakFrame != null)
                    {
                        col.Item().PaddingTop(8).Text("Peak Analysis / 峰值分析").Bold().FontSize(12);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cd => { cd.RelativeColumn(1); cd.RelativeColumn(2); });
                            AddInfoRow(table, L.Get("Report.PeakPressure"), $"{peakFrame.PeakPressureGrams:F1} g");
                            AddInfoRow(table, L.Get("Report.TotalForce"), $"{peakFrame.TotalForceGrams:F1} g");
                            AddInfoRow(table, L.Get("Report.ActivePoints"), $"{peakFrame.ActivePointCount} / 1600");
                            AddInfoRow(table, L.Get("Report.ContactArea"), $"{peakFrame.ContactAreaMm2:F2} mm²");
                        });
                    }

                    // Peak heatmap
                    if (peakHeatmapBytes != null)
                    {
                        col.Item().PaddingTop(8).Text("Peak Pressure Distribution / 峰值壓力分佈").Bold().FontSize(12);
                        col.Item().AlignCenter().Image(peakHeatmapBytes).FitWidth();
                    }
                });

                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(path);

        return path;
    }

    // ── Composition helpers ──

    private void ComposeHeader(IContainer container, string title)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                if (_logoBytes != null)
                {
                    col.Item().Height(40).Image(_logoBytes).FitHeight();
                }
                else
                {
                    col.Item().Text("TRANZX").Bold().FontSize(20).FontColor("#ED7D31");
                }
            });

            row.RelativeItem().AlignRight().Column(col =>
            {
                col.Item().Text(title).Bold().FontSize(16).FontColor("#2B5797");
                col.Item().Text($"FS-ARR-40X40-S20 | 40×40 Array").FontSize(9).FontColor("#888888");
            });
        });
    }

    private void ComposeReportInfo(IContainer container, CalibrationRecipe recipe, string operatorName, LocalizationService L)
    {
        container.Column(col =>
        {
            col.Item().PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(1);
                    cd.RelativeColumn(2);
                });

                AddInfoRow(table, L.Get("Report.Generated"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                AddInfoRow(table, L.Get("Report.Operator"), operatorName);
                AddInfoRow(table, L.Get("Report.Sensor"), "FS-ARR-40X40-S20");
                AddInfoRow(table, L.Get("Report.CalRecipe"), recipe.Name);
            });
        });
    }

    private void ComposeMeasurementSummary(IContainer container, SensorFrame frame, LocalizationService L)
    {
        container.Column(col =>
        {
            col.Item().Text(L.Get("Report.Summary")).Bold().FontSize(14).FontColor("#2B5797");
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(1);
                    cd.RelativeColumn(1);
                    cd.RelativeColumn(1);
                    cd.RelativeColumn(1);
                });

                // Header
                table.Header(h =>
                {
                    h.Cell().Background("#2B5797").Padding(6).Text(L.Get("Report.PeakPressure")).FontColor(Colors.White).Bold();
                    h.Cell().Background("#2B5797").Padding(6).Text(L.Get("Report.TotalForce")).FontColor(Colors.White).Bold();
                    h.Cell().Background("#2B5797").Padding(6).Text(L.Get("Report.ContactArea")).FontColor(Colors.White).Bold();
                    h.Cell().Background("#2B5797").Padding(6).Text(L.Get("Report.ActivePoints")).FontColor(Colors.White).Bold();
                });

                table.Cell().Background("#F5F5F5").Padding(6).Text($"{frame.PeakPressureGrams:F1} g").Bold();
                table.Cell().Background("#F5F5F5").Padding(6).Text($"{frame.TotalForceGrams:F1} g");
                table.Cell().Background("#F5F5F5").Padding(6).Text($"{frame.ContactAreaMm2:F2} mm²");
                table.Cell().Background("#F5F5F5").Padding(6).Text($"{frame.ActivePointCount} / 1600");
            });
        });
    }

    private void ComposeHeatmap(IContainer container, byte[] heatmapBytes, LocalizationService L)
    {
        container.Column(col =>
        {
            col.Item().PaddingTop(8).Text(L.Get("Report.Heatmap")).Bold().FontSize(14).FontColor("#2B5797");
            col.Item().PaddingTop(4).AlignCenter().Image(heatmapBytes).FitWidth();
        });
    }

    private void ComposeCalibrationInfo(IContainer container, CalibrationRecipe recipe, LocalizationService L)
    {
        container.Column(col =>
        {
            col.Item().PaddingTop(8).Text(L.Get("Report.Calibration")).Bold().FontSize(12);
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(cd => { cd.RelativeColumn(1); cd.RelativeColumn(2); });
                AddInfoRow(table, "Recipe ID", recipe.RecipeId);
                AddInfoRow(table, "Interpolation", recipe.Interpolation.ToString());
                AddInfoRow(table, "Cal. Date", recipe.LastCalibratedDate.ToString("yyyy-MM-dd"));
                AddInfoRow(table, "Lookup Points", recipe.LookupTable.Count.ToString());
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Text(text =>
            {
                text.Span("© 2026 TRANZX Technology | Pressure Mapping System").FontSize(8).FontColor("#888888");
            });
            row.RelativeItem().AlignRight().Text(text =>
            {
                text.Span("Page ").FontSize(8);
                text.CurrentPageNumber().FontSize(8);
                text.Span(" / ").FontSize(8);
                text.TotalPages().FontSize(8);
            });
        });
    }

    private static void AddInfoRow(TableDescriptor table, string label, string value)
    {
        table.Cell().Padding(3).Text(label).Bold().FontColor("#555555");
        table.Cell().Padding(3).Text(value);
    }

    /// <summary>
    /// 渲染熱力圖為 PNG byte[]
    /// </summary>
    private byte[] GenerateHeatmapBytes(SensorFrame frame, int width, int height)
    {
        int legendW = 80;
        int totalW = width + legendW + 20;

        using var surface = SKSurface.Create(new SKImageInfo(totalW, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        _exportService.RenderHeatmapToCanvas(canvas, frame.PressureGrams, 0, 0, width, height, _exportService.ColorScaleMax);
        _exportService.RenderColorLegend(canvas, width + 10, 30, legendW - 20, height - 60, _exportService.ColorScaleMax);

        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black, TextSize = 12, IsAntialias = true
        };
        canvas.DrawText($"Peak: {frame.PeakPressureGrams:F0}g | Total: {frame.TotalForceGrams:F0}g | " +
                        $"Active: {frame.ActivePointCount}pts", 10, 14, titlePaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
