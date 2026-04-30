using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PressureMappingSystem.Comparison.Models;

namespace PressureMappingSystem.Comparison.Controls;

/// <summary>
/// 顯示一張 ComparisonResult 的評分卡：
///   - 大字 FinalScore
///   - PASS / FAIL 標籤
///   - 「(原始 X 分)」備註（飽和到 100 時才顯示）
///   - 四個分項分數條
/// </summary>
public class ScoreCardControl : UserControl
{
    private readonly TextBlock _scoreText;
    private readonly TextBlock _scoreUnit;
    private readonly TextBlock _rawScoreNote;
    private readonly TextBlock _scenarioNote;
    private readonly TextBlock _vetoNote;
    private readonly Border _passLabel;
    private readonly TextBlock _passText;
    private readonly StackPanel _breakdown;

    public ScoreCardControl()
    {
        var brandOrange = new SolidColorBrush(Color.FromRgb(0xED, 0x7D, 0x31));
        var textPrimary = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE8));
        var textSecondary = new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0xA8));
        var panelBg = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x36));

        var root = new Border
        {
            Background = panelBg,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 10, 14, 10),
        };

        var stack = new StackPanel();
        root.Child = stack;

        // ── Score 大字 ──
        var scoreRow = new Grid();
        scoreRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        scoreRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        scoreRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var scoreStack = new StackPanel { Orientation = Orientation.Horizontal };
        _scoreText = new TextBlock
        {
            FontSize = 56,
            FontWeight = FontWeights.Bold,
            Foreground = brandOrange,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "—"
        };
        _scoreUnit = new TextBlock
        {
            FontSize = 16,
            Foreground = textSecondary,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(4, 0, 0, 12),
            Text = "/ 100"
        };
        scoreStack.Children.Add(_scoreText);
        scoreStack.Children.Add(_scoreUnit);
        Grid.SetColumn(scoreStack, 0);
        scoreRow.Children.Add(scoreStack);

        _rawScoreNote = new TextBlock
        {
            FontSize = 11,
            Foreground = textSecondary,
            FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(12, 0, 0, 14),
            Visibility = Visibility.Collapsed,
            Text = ""
        };
        Grid.SetColumn(_rawScoreNote, 1);
        scoreRow.Children.Add(_rawScoreNote);

        _passLabel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(20, 6, 20, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _passText = new TextBlock
        {
            Text = "—",
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
        };
        _passLabel.Child = _passText;
        Grid.SetColumn(_passLabel, 2);
        scoreRow.Children.Add(_passLabel);

        stack.Children.Add(scoreRow);

        // ── 應用情境資訊 (PASS/FAIL 下方一行說明)──
        _scenarioNote = new TextBlock
        {
            FontSize = 12,
            Foreground = textSecondary,
            FontFamily = new FontFamily("Microsoft JhengHei"),
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed,
            Text = ""
        };
        stack.Children.Add(_scenarioNote);

        // ── Veto 警告 (critical 觸發時才顯示，紅底白字)──
        _vetoNote = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
            FontFamily = new FontFamily("Microsoft JhengHei"),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Text = ""
        };
        stack.Children.Add(_vetoNote);

        // ── 分項條 ──
        _breakdown = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        stack.Children.Add(_breakdown);

        Content = root;
    }

    public void Update(ComparisonResult? r)
    {
        if (r == null)
        {
            _scoreText.Text = "—";
            _passText.Text = "—";
            _rawScoreNote.Visibility = Visibility.Collapsed;
            _scenarioNote.Visibility = Visibility.Collapsed;
            _vetoNote.Visibility = Visibility.Collapsed;
            _breakdown.Children.Clear();
            return;
        }

        // ── Score 大字 ──
        _scoreText.Text = r.FinalScore.ToString("F0");

        if (r.IsInPassZone && Math.Abs(r.FinalScore - r.RawScore) > 0.5)
        {
            _rawScoreNote.Text = $"（飽和 → 100，原始 {r.RawScore:F1}）";
            _rawScoreNote.Visibility = Visibility.Visible;
        }
        else
        {
            _rawScoreNote.Visibility = Visibility.Collapsed;
        }

        // ── PASS / FAIL ──
        if (r.IsPass)
        {
            _passText.Text = "PASS";
            _passLabel.Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        }
        else
        {
            _passText.Text = "FAIL";
            _passLabel.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
        }

        // ── Veto 警告（critical 觸發時顯示，並提供解釋）──
        if (r.VetoTriggered)
        {
            _vetoNote.Text = $"⚠ Critical Veto 觸發：{r.VetoReason}";
            _vetoNote.Visibility = Visibility.Visible;
        }
        else
        {
            _vetoNote.Visibility = Visibility.Collapsed;
        }

        // ── 應用情境資訊 ──
        string scenarioLabel = r.AppliedScenario switch
        {
            ScoringScenario.SimilarObject => "情境 1：相似待測物（面積為主）",
            ScoringScenario.DifferentObject => "情境 2：不同待測物（重量為主）",
            _ => "未知"
        };
        if (!string.IsNullOrEmpty(r.AutoJudgmentReason))
        {
            _scenarioNote.Text = $"📐 自動判定 → {scenarioLabel}    [{r.AutoJudgmentReason}]";
        }
        else
        {
            _scenarioNote.Text = $"📐 使用者選擇 → {scenarioLabel}";
        }
        _scenarioNote.Visibility = Visibility.Visible;

        // ── 四個分項小條（用 AppliedProfile 而非 Config.* 舊欄位）──
        var profile = r.AppliedProfile;
        _breakdown.Children.Clear();
        AddBreakdownRow("重量準度 (Weight, Total Force)",
            r.WeightScore, profile.WeightFactor, profile.WeightSum,
            $"誤差 {r.WeightErrorPercent:F2}% / 容差 ±{profile.WeightTolerancePercent:F1}%");
        AddBreakdownRow("形狀相似度 (Shape, SSIM)",
            r.ShapeScore, profile.ShapeFactor, profile.WeightSum,
            $"SSIM {r.Ssim:F4}");
        AddBreakdownRow("位置吻合 (Position, CoP)",
            r.PositionScore, profile.PositionFactor, profile.WeightSum,
            $"偏移 {r.CoPShiftMm:F2} mm / 上限 {profile.MaxCopShiftMm:F1} mm");
        AddBreakdownRow("受壓面積 (Area, IoU)",
            r.AreaScore, profile.AreaFactor, profile.WeightSum,
            $"IoU {r.IoU:F4}");
    }

    private void AddBreakdownRow(string label, double score, double weight, double weightSum, string detail)
    {
        var textPrimary = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE8));
        var textSecondary = new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0xA8));

        var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        // label + weight
        var labelStack = new StackPanel { Orientation = Orientation.Horizontal };
        labelStack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = textPrimary,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        double weightPct = weightSum > 0 ? weight / weightSum * 100 : 0;
        labelStack.Children.Add(new TextBlock
        {
            Text = $"  ×{weightPct:F0}%",
            Foreground = textSecondary,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(labelStack, 0);
        row.Children.Add(labelStack);

        // 進度條
        var bar = new Grid { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Height = 12 };
        bar.Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x3D));
        var fill = new Border
        {
            Background = ScoreColor(score),
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        // 用 Rect 寬度跟著父寬*比例：用 Width binding 不好做、改在 Loaded 後設
        bar.Children.Add(fill);
        bar.SizeChanged += (s, e) =>
        {
            fill.Width = Math.Max(0, e.NewSize.Width * Math.Clamp(score / 100.0, 0, 1));
            fill.Height = e.NewSize.Height;
        };
        Grid.SetColumn(bar, 1);
        row.Children.Add(bar);

        // 分數 + detail
        var rightStack = new StackPanel();
        rightStack.Children.Add(new TextBlock
        {
            Text = $"{score:F0} / 100",
            Foreground = textPrimary,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        rightStack.Children.Add(new TextBlock
        {
            Text = detail,
            Foreground = textSecondary,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        Grid.SetColumn(rightStack, 2);
        row.Children.Add(rightStack);

        _breakdown.Children.Add(row);
    }

    private static SolidColorBrush ScoreColor(double score)
    {
        // ≥80 綠、60~80 黃、<60 紅
        if (score >= 80) return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        if (score >= 60) return new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x44));
        return new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
    }
}
