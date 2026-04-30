using System.IO;
using PressureMappingSystem.Models;
using PressureMappingSystem.Comparison.Models;
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
    /// 接收 logo bytes 的重載 constructor（用於從 WPF pack URI / Resource 載入 logo）。
    /// 用 byte[] 比 logoPath 適合 WPF 場景，因 pack URI 不是檔案系統路徑、無法用 File.ReadAllBytes 讀取。
    /// </summary>
    public PdfReportService(ExportService exportService, string outputFolder, byte[]? logoBytes)
    {
        _exportService = exportService;
        _outputFolder = outputFolder;
        Directory.CreateDirectory(_outputFolder);

        QuestPDF.Settings.License = LicenseType.Community;

        if (logoBytes != null && logoBytes.Length > 0)
            _logoBytes = logoBytes;
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
                col.Item().Text($"PMS-1600 | 40×40 Array").FontSize(9).FontColor("#888888");
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
                AddInfoRow(table, L.Get("Report.Sensor"), "PMS-1600");
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

    // ═══════════════════════════════════════════════════════════════════════
    // 比對報告（Reference vs DUT）— 比對結果完整 PDF 報告
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 從 ComparisonResult + 兩張 CapturedSnapshot 生成完整比對 PDF 報告。
    /// 內容：第 1 頁總覽（分數、PASS/FAIL、四分項表、摘要差）、
    ///       第 2 頁三張熱力圖（A/B/差）、
    ///       第 3 頁評分配置 + 警告 + 簽核欄。
    /// </summary>
    public string GenerateComparisonReport(
        CapturedSnapshot reference,
        CapturedSnapshot dut,
        ComparisonResult result,
        string operatorName = "",
        string notes = "")
    {
        string verdict = result.IsPass ? "PASS" : "FAIL";
        string filename = $"ComparisonReport_{DateTime.Now:yyyyMMdd_HHmmss}_{verdict}.pdf";
        string path = Path.Combine(_outputFolder, filename);

        // 統一色階（兩圖 + 差圖 → 兩圖共用 max(A.Peak, B.Peak)；差圖獨立用自己的 max）
        double scaleMax = Math.Max(reference.Peak, dut.Peak);
        if (scaleMax < 1) scaleMax = 1;
        double diffScaleMax = result.MaxAbsDiff > 0 ? result.MaxAbsDiff : 1;

        byte[] heatmapA = RenderSnapshotHeatmap(reference, scaleMax, "A - Reference");
        byte[] heatmapB = RenderSnapshotHeatmap(dut, scaleMax, "B - DUT");
        byte[] heatmapDiff = RenderSnapshotHeatmap(reference, diffScaleMax, "|A - B|", absDiff: result.AbsDiff);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(c => ComposeHeader(c, "Pressure Comparison Report"));

                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Element(c => ComposeComparisonInfo(c, reference, dut, result, operatorName));
                    col.Item().Element(c => ComposeComparisonVerdict(c, result));
                    col.Item().Element(c => ComposeScoreBreakdown(c, result));
                    col.Item().Element(c => ComposeSummaryDelta(c, result));
                });

                page.Footer().Element(ComposeFooter);
            });

            // ── 第 2 頁：三張熱力圖 ──
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(c => ComposeHeader(c, "Pressure Comparison Report — Heatmaps"));

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Text("Snapshot Heatmaps").Bold().FontSize(14).FontColor("#2B5797");
                    col.Item().Text("A 與 B 共用色階以利比較；差圖獨立色階以呈現差異分佈。")
                        .FontSize(9).FontColor("#666666");

                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Image(heatmapA).FitWidth();
                        row.ConstantItem(8);
                        row.RelativeItem().Image(heatmapB).FitWidth();
                    });

                    col.Item().PaddingTop(8).AlignCenter().Width(450).Image(heatmapDiff).FitWidth();
                });

                page.Footer().Element(ComposeFooter);
            });

            // ── 第 3 頁：評分配置 + 警告 + 簽核 ──
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(c => ComposeHeader(c, "Pressure Comparison Report — Configuration"));

                page.Content().Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Element(c => ComposeScoringConfig(c, result));
                    col.Item().Element(c => ComposeScoringCurveExplanation(c));

                    if (result.Warnings.Count > 0)
                        col.Item().Element(c => ComposeWarnings(c, result));

                    if (!string.IsNullOrEmpty(notes))
                    {
                        col.Item().PaddingTop(6).Column(noteCol =>
                        {
                            noteCol.Item().Text("Notes / 備註").Bold().FontSize(12).FontColor("#2B5797");
                            noteCol.Item().PaddingTop(4).Text(notes).FontSize(9);
                        });
                    }

                    col.Item().PaddingTop(20).Element(ComposeSignatures);
                });

                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf(path);

        return path;
    }

    // ── Composition helpers (比對專用) ──

    private void ComposeComparisonInfo(IContainer container,
        CapturedSnapshot a, CapturedSnapshot b, ComparisonResult r, string operatorName)
    {
        container.PaddingTop(8).Table(table =>
        {
            table.ColumnsDefinition(cd =>
            {
                cd.RelativeColumn(1);
                cd.RelativeColumn(2);
                cd.RelativeColumn(1);
                cd.RelativeColumn(2);
            });

            string scenarioLabel = r.AppliedScenario switch
            {
                ScoringScenario.SimilarObject => "情境 1：相似待測物（面積為主）",
                ScoringScenario.DifferentObject => "情境 2：不同待測物（重量為主）",
                _ => r.AppliedScenario.ToString()
            };

            AddInfoRow(table, "Generated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            AddInfoRow(table, "Operator", string.IsNullOrEmpty(operatorName) ? "-" : operatorName);
            AddInfoRow(table, "Sensor", "PMS-1600");
            AddInfoRow(table, "Mode", "Reference vs DUT");
            AddInfoRow(table, "A captured", a.CapturedAt.ToString("HH:mm:ss"));
            AddInfoRow(table, "B captured", b.CapturedAt.ToString("HH:mm:ss"));
            AddInfoRow(table, "Scenario", scenarioLabel);
            AddInfoRow(table, "Auto judge", string.IsNullOrEmpty(r.AutoJudgmentReason) ? "(manual)" : r.AutoJudgmentReason);
        });
    }

    private void ComposeComparisonVerdict(IContainer container, ComparisonResult r)
    {
        string passColor = r.IsPass ? "#4CAF50" : "#E53935";
        string verdictText = r.IsPass ? "PASS" : "FAIL";

        container.PaddingTop(6).Background("#F8F8FB").Padding(12).Row(row =>
        {
            row.RelativeItem(1).Column(col =>
            {
                col.Item().Text("Final Score").FontSize(11).FontColor("#666666");
                col.Item().Text(r.FinalScore.ToString("F0")).Bold().FontSize(48).FontColor("#ED7D31");
                if (r.IsInPassZone && Math.Abs(r.FinalScore - r.RawScore) > 0.5)
                {
                    col.Item().Text($"(saturated → 100, raw {r.RawScore:F1})")
                        .FontSize(8).FontColor("#888888");
                }
                else
                {
                    col.Item().Text($"(raw {r.RawScore:F1})").FontSize(8).FontColor("#888888");
                }
            });

            row.RelativeItem(1).AlignRight().AlignMiddle().Column(col =>
            {
                col.Item().Background(passColor).Padding(16).AlignCenter().Text(verdictText)
                    .Bold().FontSize(36).FontColor(Colors.White);
                col.Item().PaddingTop(4).AlignRight().Text(
                    $"Pass threshold: {r.Config.PassThreshold:F0}    " +
                    $"Pass-zone: {r.Config.PassZoneThreshold:F0}")
                    .FontSize(8).FontColor("#888888");
            });

            // Critical Veto 提示（觸發時才顯示）
            if (r.VetoTriggered)
            {
                col.Item().PaddingTop(4).Background("#E53935").Padding(10).Column(vetoCol =>
                {
                    vetoCol.Item().Text("⚠ Critical Veto Triggered / 一票否決")
                        .Bold().FontSize(12).FontColor(Colors.White);
                    vetoCol.Item().PaddingTop(2).Text(r.VetoReason)
                        .FontSize(10).FontColor(Colors.White);
                    vetoCol.Item().PaddingTop(4).Text(
                        "說明：即使加權總分達標，當關鍵指標嚴重失常時，依品管實務一票否決原則仍判 FAIL。")
                        .FontSize(8).FontColor("#FFE0E0").Italic();
                });
            }
        });
    }

    private void ComposeScoreBreakdown(IContainer container, ComparisonResult r)
    {
        var p = r.AppliedProfile;
        double sum = p.WeightSum;

        container.PaddingTop(4).Column(col =>
        {
            col.Item().Text("Score Breakdown / 分項評分").Bold().FontSize(13).FontColor("#2B5797");
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(3);   // Item
                    cd.RelativeColumn(1);   // Score
                    cd.RelativeColumn(1);   // Weight
                    cd.RelativeColumn(3);   // Detail
                });

                table.Header(h =>
                {
                    h.Cell().Background("#2B5797").Padding(6).Text("Metric").FontColor(Colors.White).Bold();
                    h.Cell().Background("#2B5797").Padding(6).AlignRight().Text("Score").FontColor(Colors.White).Bold();
                    h.Cell().Background("#2B5797").Padding(6).AlignRight().Text("Weight").FontColor(Colors.White).Bold();
                    h.Cell().Background("#2B5797").Padding(6).Text("Detail").FontColor(Colors.White).Bold();
                });

                AddBreakdownRow(table, "Weight (Total Force) 重量準度",
                    r.WeightScore, p.WeightFactor, sum,
                    $"err {r.WeightErrorPercent:F2}% / tol ±{p.WeightTolerancePercent:F1}%");
                AddBreakdownRow(table, "Shape (SSIM) 形狀相似度",
                    r.ShapeScore, p.ShapeFactor, sum,
                    $"SSIM {r.Ssim:F4}");
                AddBreakdownRow(table, "Position (CoP) 位置吻合",
                    r.PositionScore, p.PositionFactor, sum,
                    $"shift {r.CoPShiftMm:F2} mm / max {p.MaxCopShiftMm:F1} mm");
                AddBreakdownRow(table, "Area (IoU) 受壓面積",
                    r.AreaScore, p.AreaFactor, sum,
                    $"IoU {r.IoU:F4}");
            });
        });
    }

    private void ComposeSummaryDelta(IContainer container, ComparisonResult r)
    {
        container.PaddingTop(4).Column(col =>
        {
            col.Item().Text("Summary Delta / 摘要差").Bold().FontSize(13).FontColor("#2B5797");
            col.Item().PaddingTop(4).Table(table =>
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
                    h.Cell().Background("#888888").Padding(5).Text("ΔPeak").FontColor(Colors.White).Bold();
                    h.Cell().Background("#888888").Padding(5).Text("ΔTotal").FontColor(Colors.White).Bold();
                    h.Cell().Background("#888888").Padding(5).Text("CoP shift").FontColor(Colors.White).Bold();
                    h.Cell().Background("#888888").Padding(5).Text("MAE").FontColor(Colors.White).Bold();
                });

                table.Cell().Background("#F5F5F5").Padding(5).Text($"{r.PeakDelta:+0.0;-0.0;0} g");
                table.Cell().Background("#F5F5F5").Padding(5).Text($"{r.TotalForceDelta:+0.0;-0.0;0} g");
                table.Cell().Background("#F5F5F5").Padding(5).Text($"{r.CoPShiftMm:F2} mm");
                table.Cell().Background("#F5F5F5").Padding(5).Text($"{r.MAE:F3} g");
            });
        });
    }

    private void ComposeScoringConfig(IContainer container, ComparisonResult r)
    {
        var p = r.AppliedProfile;
        container.Column(col =>
        {
            col.Item().Text("Scoring Configuration").Bold().FontSize(14).FontColor("#2B5797");
            col.Item().PaddingTop(4).Text(
                $"Selected: {r.Config.Scenario}    Applied: {r.AppliedScenario}    " +
                $"Auto threshold: IoU = {r.Config.AutoThresholdIou:F2}")
                .FontSize(9).FontColor("#555555");

            col.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(2);
                    cd.RelativeColumn(1);
                });
                AddInfoRow(table, "Weight factor", $"{p.WeightFactor:F2}");
                AddInfoRow(table, "Shape factor",  $"{p.ShapeFactor:F2}");
                AddInfoRow(table, "Position factor", $"{p.PositionFactor:F2}");
                AddInfoRow(table, "Area factor",   $"{p.AreaFactor:F2}");
                AddInfoRow(table, "Weight tolerance", $"±{p.WeightTolerancePercent:F1}%");
                AddInfoRow(table, "Max CoP shift", $"{p.MaxCopShiftMm:F2} mm");
                AddInfoRow(table, "Pass-zone threshold (saturate→100)", $"{r.Config.PassZoneThreshold:F0}");
                AddInfoRow(table, "Pass threshold", $"{r.Config.PassThreshold:F0}");
            });
        });
    }

    private void ComposeScoringCurveExplanation(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Text("Scoring Curve / 評分曲線").Bold().FontSize(13).FontColor("#2B5797");
            col.Item().PaddingTop(4).Text(
                "Shape / Area / Position 分項採用業界非線性遞減曲線（分段線性）。" +
                "Weight 分項採用線性 Tolerance 衰減。")
                .FontSize(9).FontColor("#555555");

            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(1);
                    cd.RelativeColumn(1);
                    cd.RelativeColumn(2);
                });
                table.Header(h =>
                {
                    h.Cell().Background("#888888").Padding(4).Text("相似度").FontColor(Colors.White).Bold();
                    h.Cell().Background("#888888").Padding(4).Text("分數").FontColor(Colors.White).Bold();
                    h.Cell().Background("#888888").Padding(4).Text("區段").FontColor(Colors.White).Bold();
                });
                AddCurveRow(table, "1.00", "100", "Perfect");
                AddCurveRow(table, "0.85", "95",  "進入合格飽和區");
                AddCurveRow(table, "0.70", "80",  "緩降區");
                AddCurveRow(table, "0.50", "50",  "陡降區");
                AddCurveRow(table, "0.30", "0",   "Floor");
            });
        });
    }

    private void ComposeWarnings(IContainer container, ComparisonResult r)
    {
        container.Background("#FFF8E1").Padding(8).Column(col =>
        {
            col.Item().Text("⚠ Warnings / 提醒").Bold().FontSize(12).FontColor("#E65100");
            foreach (var w in r.Warnings)
                col.Item().PaddingTop(2).Text($"• {w}").FontSize(9).FontColor("#5D4037");
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Text("Signatures / 簽核").Bold().FontSize(12).FontColor("#2B5797");
            col.Item().PaddingTop(20).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().BorderBottom(1).BorderColor("#888888").Height(1);
                    c.Item().PaddingTop(4).AlignCenter().Text("Operator / 操作員")
                        .FontSize(9).FontColor("#666666");
                });
                row.ConstantItem(20);
                row.RelativeItem().Column(c =>
                {
                    c.Item().BorderBottom(1).BorderColor("#888888").Height(1);
                    c.Item().PaddingTop(4).AlignCenter().Text("Reviewer / 主管")
                        .FontSize(9).FontColor("#666666");
                });
                row.ConstantItem(20);
                row.RelativeItem().Column(c =>
                {
                    c.Item().BorderBottom(1).BorderColor("#888888").Height(1);
                    c.Item().PaddingTop(4).AlignCenter().Text("Date / 日期")
                        .FontSize(9).FontColor("#666666");
                });
            });
        });
    }

    private static void AddBreakdownRow(TableDescriptor table,
        string label, double score, double weight, double weightSum, string detail)
    {
        double weightPct = weightSum > 0 ? weight / weightSum * 100 : 0;
        string scoreColor = score >= 80 ? "#4CAF50" : (score >= 60 ? "#FFAA44" : "#E53935");

        table.Cell().Background("#F5F5F5").Padding(6).Text(label).FontSize(10);
        table.Cell().Background("#F5F5F5").Padding(6).AlignRight().Text($"{score:F1} / 100")
            .FontSize(10).Bold().FontColor(scoreColor);
        table.Cell().Background("#F5F5F5").Padding(6).AlignRight().Text($"{weightPct:F0}%").FontSize(10);
        table.Cell().Background("#F5F5F5").Padding(6).Text(detail).FontSize(9).FontColor("#555555");
    }

    private static void AddCurveRow(TableDescriptor table, string sim, string score, string note)
    {
        table.Cell().Background("#FAFAFA").Padding(4).Text(sim).FontSize(9);
        table.Cell().Background("#FAFAFA").Padding(4).Text(score).FontSize(9).Bold();
        table.Cell().Background("#FAFAFA").Padding(4).Text(note).FontSize(9).FontColor("#555555");
    }

    /// <summary>
    /// 把 CapturedSnapshot 渲染成 PNG（重用 ExportService.RenderHeatmapToCanvas）。
    /// 若提供 absDiff，則改用該矩陣（顯示差圖）。
    /// </summary>
    private byte[] RenderSnapshotHeatmap(CapturedSnapshot snapshot, double colorScaleMax,
        string title, double[,]? absDiff = null)
    {
        const int width = 540;
        const int height = 540;
        const int legendW = 70;
        int totalW = width + legendW + 16;
        int totalH = height + 28; // 留 title 空間

        using var surface = SKSurface.Create(new SKImageInfo(totalW, totalH));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        var data = absDiff ?? snapshot.PressureGrams;
        _exportService.RenderHeatmapToCanvas(canvas, data, 0, 24, width, height, colorScaleMax);
        _exportService.RenderColorLegend(canvas, width + 8, 24 + 20, legendW - 16, height - 40, colorScaleMax);

        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 14,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Microsoft JhengHei", SKFontStyle.Bold)
                       ?? SKTypeface.Default
        };
        canvas.DrawText(title, 8, 18, titlePaint);

        if (absDiff == null)
        {
            using var infoPaint = new SKPaint
            {
                Color = SKColors.DarkGray,
                TextSize = 11,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Consolas") ?? SKTypeface.Default
            };
            canvas.DrawText(
                $"Peak {snapshot.Peak:F0}g  Total {snapshot.TotalForce / 1000:F2}kg  Active {snapshot.ActivePoints}",
                160, 18, infoPaint);
        }

        using var image = surface.Snapshot();
        using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
        return pngData.ToArray();
    }
}
