using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RiftReview.App.Converters;

// Converts a bool (Win) to a brush. Pass true→TrueBrush else FalseBrush; defaults win=green, loss=red.
public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = Brushes.LimeGreen;
    public Brush FalseBrush { get; set; } = Brushes.IndianRed;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? TrueBrush : FalseBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
