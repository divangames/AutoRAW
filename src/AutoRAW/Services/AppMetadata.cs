using System.Reflection;

namespace AutoRAW.Services;

/// <summary>
/// Версия продукта: из верхней записи <c>CHANGELOG.md</c> (<c>## [a.b.c.d.e]</c>, недостающие справа — нули).
/// В UI — полная пятизначная строка из <c>AssemblyInformationalVersion</c>; для CLR — первые четыре компонента.
/// </summary>
public static class AppMetadata
{
    private static readonly Lazy<ProductVersion> _productVersion = new(ReadProductVersion);

    /// <summary>Полная версия для заголовка окна, «О программе», сравнения с релизом (например <c>0.7.6.0.0</c>).</summary>
    public static string DisplayVersion => _productVersion.Value.ToString();

    /// <summary>Пятикомпонентная версия — для сравнения с опубликованными обновлениями.</summary>
    public static ProductVersion AppVersion => _productVersion.Value;

    /// <summary>Первые четыре компонента как <see cref="Version"/> (совместимость, User-Agent).</summary>
    public static Version AssemblyVersion => _productVersion.Value.ToAssemblyVersion();

    private static ProductVersion ReadProductVersion()
    {
        var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (ProductVersion.TryParse(info, out var v))
            return v;

        var av = Assembly.GetExecutingAssembly().GetName().Version;
        if (av is null)
            return new ProductVersion(0, 0, 0, 0, 0);
        return new ProductVersion(av.Major, av.Minor, av.Build, av.Revision, 0);
    }
}
