using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AutoRAW.Models;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;

namespace AutoRAW.Converters;

/// <summary>Возвращает цвет акцентной полосы слева у сообщения в зависимости от типа.</summary>
[ValueConversion(typeof(LogLineKind), typeof(WpfBrush))]
public sealed class LogKindToAccentBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is LogLineKind k ? k : LogLineKind.Normal) switch
        {
            LogLineKind.Zona   => new SolidColorBrush(WpfColor.FromRgb(0xC7, 0x92, 0xEA)),
            LogLineKind.Done   => new SolidColorBrush(WpfColor.FromRgb(0x5C, 0xB8, 0x7A)),
            LogLineKind.Error  => new SolidColorBrush(WpfColor.FromRgb(0xF0, 0x70, 0x70)),
            LogLineKind.Cancel => new SolidColorBrush(WpfColor.FromRgb(0xF0, 0x70, 0x70)),
            LogLineKind.Pause  => new SolidColorBrush(WpfColor.FromRgb(0x6B, 0xB0, 0xFF)),
            _                  => new SolidColorBrush(WpfColor.FromRgb(0x60, 0x60, 0x64)),
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}

/// <summary>
/// Возвращает Unicode-символ иконки типа сообщения.
/// WPF не поддерживает цветные emoji — используем простые символы + цвет через Foreground.
/// </summary>
[ValueConversion(typeof(LogLineKind), typeof(string))]
public sealed class LogKindToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is LogLineKind k ? k : LogLineKind.Normal) switch
        {
            LogLineKind.Zona   => "◆",   // ромб — персонаж ZONA
            LogLineKind.Done   => "✔",   // галочка — успех
            LogLineKind.Error  => "✖",   // крест — ошибка
            LogLineKind.Cancel => "⚠",   // предупреждение — отмена
            LogLineKind.Pause  => "❙❙",  // пауза
            _                  => "●",   // точка — информация
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
