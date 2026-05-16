using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace AutoRAW.Converters;

/// <summary>
/// Прикреплённое свойство BindableInlines для TextBlock,
/// позволяющее биндить коллекцию Inline через ZonaTextConverter.
/// </summary>
public static class ZonaInlines
{
    public static readonly DependencyProperty BindableInlinesProperty =
        DependencyProperty.RegisterAttached(
            "BindableInlines",
            typeof(IEnumerable<Inline>),
            typeof(ZonaInlines),
            new PropertyMetadata(null, OnChanged));

    public static void SetBindableInlines(TextBlock element, IEnumerable<Inline> value) =>
        element.SetValue(BindableInlinesProperty, value);

    public static IEnumerable<Inline> GetBindableInlines(TextBlock element) =>
        (IEnumerable<Inline>)element.GetValue(BindableInlinesProperty);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.Inlines.Clear();
        if (e.NewValue is IEnumerable<Inline> inlines)
            tb.Inlines.AddRange(inlines);
    }
}

/// <summary>Разбирает **bold** разметку в список <see cref="Inline"/>.</summary>
[ValueConversion(typeof(string), typeof(IEnumerable<Inline>))]
public sealed class ZonaTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string ?? string.Empty;
        var inlines = new List<Inline>();
        var pos = 0;

        while (pos < text.Length)
        {
            var boldStart = text.IndexOf("**", pos, StringComparison.Ordinal);
            if (boldStart < 0)
            {
                inlines.Add(new Run(text[pos..]));
                break;
            }

            if (boldStart > pos)
                inlines.Add(new Run(text[pos..boldStart]));

            var boldEnd = text.IndexOf("**", boldStart + 2, StringComparison.Ordinal);
            if (boldEnd < 0)
            {
                inlines.Add(new Run(text[boldStart..]));
                break;
            }

            inlines.Add(new Bold(new Run(text[(boldStart + 2)..boldEnd])));
            pos = boldEnd + 2;
        }

        return inlines;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
