using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using RiftReview.Core.Analysis;

namespace RiftReview.App.Controls;

/// <summary>
/// Pure data-to-pixel scaler. No UI dependencies — fully unit-testable without an STA thread.
/// </summary>
public sealed class ChartScaler
{
    private readonly double _minX, _maxX, _minY, _maxY, _w, _h, _pad;

    public ChartScaler(double minX, double maxX, double minY, double maxY, double width, double height, double pad)
    {
        _minX = minX; _maxX = maxX; _minY = minY; _maxY = maxY;
        _w = width; _h = height; _pad = pad;
    }

    public double X(double x) =>
        _pad + (_maxX <= _minX ? 0 : (x - _minX) / (_maxX - _minX)) * (_w - 2 * _pad);

    public double Y(double y) =>
        _pad + (_maxY <= _minY ? 0 : (1 - (y - _minY) / (_maxY - _minY))) * (_h - 2 * _pad);
}

/// <summary>
/// A single data series to render in a <see cref="LineChart"/>.
/// </summary>
public sealed record ChartSeries(IReadOnlyList<ChartPoint> Points, Brush Stroke, bool Dashed = false);

/// <summary>
/// Hand-rolled WPF line-chart control. Renders via OnRender — no template or XAML required.
/// Visual output is verified at the Task 15 screenshot gate.
/// </summary>
public sealed class LineChart : FrameworkElement
{
    // ── Static chrome brushes / pens (frozen once, reused every render) ────────
    private static readonly Brush ChromeBrush  = Freeze(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush LabelBrush   = Freeze(new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)));
    private static readonly Pen   ZeroPen      = FreezePen(new Pen(ChromeBrush, 1));
    private static readonly Brush DeathBrush   = Freeze(new SolidColorBrush(Color.FromRgb(0xE2, 0x4A, 0x4A)));
    private static readonly Pen   DeathLinePen = FreezePen(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0x80, 0xE2, 0x4A, 0x4A))), 1.5));
    private static readonly Pen   DeathXPen    = FreezePen(new Pen(DeathBrush, 2));

    private const double Pad = 28;

    // ── Dependency properties ──────────────────────────────────────────────────

    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IReadOnlyList<ChartSeries>), typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ChartSeries>? Series
    {
        get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public static readonly DependencyProperty DeathMarkersProperty = DependencyProperty.Register(
        nameof(DeathMarkers), typeof(IReadOnlyList<double>), typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? DeathMarkers
    {
        get => (IReadOnlyList<double>?)GetValue(DeathMarkersProperty);
        set => SetValue(DeathMarkersProperty, value);
    }

    public static readonly DependencyProperty ShowZeroLineProperty = DependencyProperty.Register(
        nameof(ShowZeroLine), typeof(bool), typeof(LineChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool ShowZeroLine
    {
        get => (bool)GetValue(ShowZeroLineProperty);
        set => SetValue(ShowZeroLineProperty, value);
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var series = Series?.Where(s => s.Points.Count > 0).ToList() ?? new List<ChartSeries>();
        var pts = series.SelectMany(s => s.Points).ToList();

        if (pts.Count == 0)
        {
            DrawText(dc, "No data", new Point(Pad, h / 2 - 8), LabelBrush);
            return;
        }

        double minX = pts.Min(p => p.Minute), maxX = pts.Max(p => p.Minute);
        double minY = pts.Min(p => p.Value),  maxY = pts.Max(p => p.Value);

        if (ShowZeroLine) { minY = System.Math.Min(minY, 0); maxY = System.Math.Max(maxY, 0); }
        if (maxY <= minY) maxY = minY + 1;
        if (maxX <= minX) maxX = minX + 1;

        var sc = new ChartScaler(minX, maxX, minY, maxY, w, h, Pad);

        // Zero gridline
        if (ShowZeroLine && minY <= 0 && maxY >= 0)
        {
            double zy = sc.Y(0);
            dc.DrawLine(ZeroPen, new Point(Pad, zy), new Point(w - Pad, zy));
        }

        // Series polylines
        foreach (var s in series)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                var p0 = s.Points[0];
                ctx.BeginFigure(new Point(sc.X(p0.Minute), sc.Y(p0.Value)), false, false);
                for (int i = 1; i < s.Points.Count; i++)
                    ctx.LineTo(new Point(sc.X(s.Points[i].Minute), sc.Y(s.Points[i].Value)), true, false);
            }
            geo.Freeze();

            var pen = new Pen(s.Stroke, 2);
            if (s.Dashed) pen.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);
            pen.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        // Death markers — vertical dashed line + ✕ at top
        if (DeathMarkers is { } deaths)
        {
            foreach (var m in deaths)
            {
                if (m < minX || m > maxX) continue;
                double x = sc.X(m);
                dc.DrawLine(DeathLinePen, new Point(x, Pad), new Point(x, h - Pad));
                const double k = 4;
                dc.DrawLine(DeathXPen, new Point(x - k, Pad - k), new Point(x + k, Pad + k));
                dc.DrawLine(DeathXPen, new Point(x - k, Pad + k), new Point(x + k, Pad - k));
            }
        }

        // Minimal axis labels
        DrawText(dc, Fmt(maxY), new Point(2, sc.Y(maxY) - 7), LabelBrush);
        DrawText(dc, Fmt(minY), new Point(2, sc.Y(minY) - 7), LabelBrush);
        DrawText(dc, $"{maxX:0}m", new Point(w - Pad, h - Pad + 4), LabelBrush);
    }

    private void DrawText(DrawingContext dc, string text, Point at, Brush brush)
    {
        var ft = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            11,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, at);
    }

    private static string Fmt(double v) =>
        System.Math.Abs(v) >= 1000 ? $"{v / 1000:0.0}k" : $"{v:0}";

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
    private static Pen FreezePen(Pen p) { p.Freeze(); return p; }
}
