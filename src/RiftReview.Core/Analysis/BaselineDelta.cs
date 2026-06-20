namespace RiftReview.Core.Analysis;

public readonly record struct BaselineDeltaResult(string Text, bool IsGood, bool IsBad);

public static class BaselineDelta
{
    public static BaselineDeltaResult Compute(double current, double baseline, int dir, string unit)
    {
        double raw = current - baseline;
        bool isPct = unit == "%";
        double shown = isPct ? raw * 100.0 : raw;
        string mag = isPct ? $"{System.Math.Abs(shown):0}%" : $"{System.Math.Abs(shown):0.0}";
        string sign = shown >= 0 ? "+" : "−"; // − is U+2212
        string text = sign + mag;

        bool better = dir >= 0 ? raw > 0 : raw < 0;
        bool worse  = dir >= 0 ? raw < 0 : raw > 0;
        return new BaselineDeltaResult(text, better, worse);
    }
}
