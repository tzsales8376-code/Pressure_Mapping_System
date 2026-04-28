using PressureMappingSystem.Models;

namespace PressureMappingSystem.Comparison.Models;

/// <summary>
/// 從即時量測中擷取的一張壓力快照（A 或 B）。
/// 跟 SensorFrame 不同：這是「使用者刻意按下凍結後選擇保留」的快照，
/// 帶有時間戳記與摘要資訊，方便比對視窗顯示與報告。
/// </summary>
public class CapturedSnapshot
{
    /// <summary>擷取時的本機時間</summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>來源 frame 的 sequence number（如可取得）</summary>
    public long FrameIndex { get; set; }

    /// <summary>40×40 壓力資料的深拷貝（單位：g）</summary>
    public double[,] PressureGrams { get; set; } = new double[40, 40];

    /// <summary>標籤（"A" 或 "B"）</summary>
    public string Label { get; set; } = "";

    /// <summary>備註（使用者可填）</summary>
    public string Notes { get; set; } = "";

    // ── 預先計算的摘要（避免每次比對重算）──
    public double Peak { get; set; }
    public double TotalForce { get; set; }
    public int ActivePoints { get; set; }
    public double ContactArea { get; set; }
    public double CoPX { get; set; }
    public double CoPY { get; set; }

    /// <summary>從 SensorFrame 建立快照（深拷貝資料、預先計算摘要）</summary>
    public static CapturedSnapshot FromFrame(SensorFrame frame, string label, double cellAreaMm = 0.3)
    {
        var snap = new CapturedSnapshot
        {
            CapturedAt = DateTime.Now,
            FrameIndex = frame.FrameIndex,
            Label = label,
        };

        // 深拷貝 + 即時計算摘要
        double sum = 0, peak = 0;
        int active = 0;
        double cySum = 0, cxSum = 0, weightSum = 0;
        for (int r = 0; r < 40; r++)
            for (int c = 0; c < 40; c++)
            {
                double v = frame.PressureGrams[r, c];
                snap.PressureGrams[r, c] = v;
                if (v > 0)
                {
                    sum += v;
                    if (v > peak) peak = v;
                    active++;
                    cySum += r * v;
                    cxSum += c * v;
                    weightSum += v;
                }
            }
        snap.Peak = peak;
        snap.TotalForce = sum;
        snap.ActivePoints = active;
        snap.ContactArea = active * cellAreaMm * cellAreaMm;
        if (weightSum > 0)
        {
            snap.CoPY = cySum / weightSum;
            snap.CoPX = cxSum / weightSum;
        }
        return snap;
    }
}
