using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AutoRAW.Converters;

/// <summary>Строка сетки под превью: <see cref="GridUnitType.Star"/> если превью включено, иначе нулевая высота.</summary>
public sealed class BoolToPreviewRowLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
