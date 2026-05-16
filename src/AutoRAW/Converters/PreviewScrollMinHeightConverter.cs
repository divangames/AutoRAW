using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace AutoRAW.Converters;

/// <summary>
/// Минимальная высота области под скролл: как минимум высота вьюпорта, пока включено превью
/// (чтобы блок превью мог заполнять пустое место под выключенными панелями).
/// </summary>
public sealed class PreviewScrollMinHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var vh = values.ElementAtOrDefault(0) is double d ? d : 0d;
        var previewOn = values.ElementAtOrDefault(1) is bool b && b;
        return previewOn ? Math.Max(0, vh) : 0d;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
