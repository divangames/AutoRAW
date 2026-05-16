using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AutoRAW.Converters;

/// <summary>
/// Ограничивает ширину пузыря (~76% ширины ListBox) — как в Telegram, с переносом строк.
/// </summary>
[ValueConversion(typeof(double), typeof(double))]
public sealed class ChatBubbleMaxWidthConverter : IValueConverter
{
    private const double WidthRatio = 0.76;
    private const double MinBubble = 120;
    private const double SideChrome = 56;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double w && !double.IsNaN(w) && w > 0)
            return Math.Max(MinBubble, w * WidthRatio - SideChrome);

        return 320.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
