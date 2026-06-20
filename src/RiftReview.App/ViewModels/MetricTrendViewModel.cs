using System;
using System.Collections.Generic;
using RiftReview.Core.Analysis;

namespace RiftReview.App.ViewModels;

public sealed class MetricTrendViewModel
{
    public MetricTrendViewModel(MetricTrend t)
    {
        Key = t.Key;
        DisplayName = t.DisplayName;
        Current = Format(t.Current, t.Unit);
        Verdict = t.Verdict switch
        {
            TrendVerdict.Improving => "Improving",
            TrendVerdict.Declining => "Declining",
            TrendVerdict.Building  => "Building",
            _                      => "Steady",
        };
        IsGood = t.Verdict == TrendVerdict.Improving;
        IsBad  = t.Verdict == TrendVerdict.Declining;
        Delta  = t.Verdict == TrendVerdict.Building || !t.Current.HasValue || !t.Prior.HasValue
            ? "" : FormatDelta(t.ImprovementDelta, t.Direction, t.Unit);
        Series = t.RollingSeries;
    }

    public string Key         { get; }
    public string DisplayName { get; }
    public string Current     { get; }
    public string Verdict     { get; }
    public string Delta       { get; }
    public bool   IsGood      { get; }
    public bool   IsBad       { get; }
    public IReadOnlyList<double?> Series { get; }

    // Direction-aware comparison vs the active baseline (rank or own); null/false when no baseline.
    public bool    HasBaseline     { get; set; }
    public double? BaselineValue   { get; set; }
    public string  BaselineLabel   { get; set; } = "";   // e.g. "Gold avg" / "Your avg"
    public string  DeltaVsBaseline { get; set; } = "";   // "+0.8" / "−1.2", signed
    public bool    BaselineIsGood  { get; set; }
    public bool    BaselineIsBad   { get; set; }

    private static string Format(double? v, string unit) => v is null ? "—"
        : unit == "%" ? Math.Round(v.Value * 100) + "%"
        : unit == "g" ? (v.Value >= 0 ? "+" : "") + Math.Round(v.Value) + "g"
        : Math.Round(v.Value, 1).ToString();

    // ImprovementDelta is already sign-adjusted (positive = better). Show the raw metric change.
    private static string FormatDelta(double improvementDelta, int dir, string unit)
    {
        double raw  = improvementDelta * dir;          // back to metric units, signed
        string sign = raw > 0 ? "+" : raw < 0 ? "" : "";
        return unit == "%" ? sign + Math.Round(raw * 100) + " pts"
             : unit == "g" ? sign + Math.Round(raw) + "g"
             : sign + Math.Round(raw, 1);
    }
}
