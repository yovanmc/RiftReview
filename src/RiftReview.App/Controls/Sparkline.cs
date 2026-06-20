using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace RiftReview.App.Controls;

// Minimal gold polyline over a value series. Null values are skipped (gaps).
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<int?>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<int?>? Points
    {
        get => (IReadOnlyList<int?>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double?>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double?>? Values
    {
        get => (IReadOnlyList<double?>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public static readonly DependencyProperty BaselineProperty = DependencyProperty.Register(
        nameof(Baseline), typeof(double?), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double? Baseline
    {
        get => (double?)GetValue(BaselineProperty);
        set => SetValue(BaselineProperty, value);
    }

    public static readonly DependencyProperty BaselineBrushProperty = DependencyProperty.Register(
        nameof(BaselineBrush), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? BaselineBrush
    {
        get => (Brush?)GetValue(BaselineBrushProperty);
        set => SetValue(BaselineBrushProperty, value);
    }

    private static readonly Pen GoldPen = MakePen();
    private static Pen MakePen()
    {
        var p = new Pen(new SolidColorBrush(Color.FromRgb(0xC8, 0xAA, 0x6E)), 1.5);
        p.Freeze();
        return p;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var pts = (Values is not null
                ? Values.Where(v => v.HasValue).Select(v => v!.Value)
                : Points?.Where(v => v.HasValue).Select(v => (double)v!.Value))
            ?.ToList();
        if (pts is null || pts.Count < 2) return;
        double w = ActualWidth, h = ActualHeight;
        double min = pts.Min(), max = pts.Max(), range = max - min < 1e-6 ? 1 : max - min;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (int i = 0; i < pts.Count; i++)
            {
                double x = pts.Count == 1 ? 0 : i * (w / (pts.Count - 1));
                double y = h - ((pts[i] - min) / range) * h;
                if (i == 0) g.BeginFigure(new Point(x, y), false, false);
                else g.LineTo(new Point(x, y), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, GoldPen, geo);

        if (Baseline is double b && BaselineBrush is Brush bb)
        {
            double yb = h - ((b - min) / range) * h;
            if (yb >= 0 && yb <= h) // only draw when the baseline falls within the rendered band
            {
                var pen = new Pen(bb, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
                pen.Freeze();
                dc.DrawLine(pen, new Point(0, yb), new Point(w, yb));
            }
        }
    }
}
