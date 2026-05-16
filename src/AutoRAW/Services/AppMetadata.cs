using System.Reflection;

namespace AutoRAW.Services;

/// <summary>
/// Версия продукта: сборка задаётся из верхней записи <c>CHANGELOG.md</c> (<c>## [x.y.z]</c>).
/// В UI — только <c>Major.Minor.Build</c> из сборки.
/// </summary>
public static class AppMetadata
{
    private static readonly Lazy<string> _display = new(ReadDisplay);
    private static readonly Lazy<Version> _assemblyVersion = new(ReadAssemblyVersion);

    /// <summary>Короткая строка для заголовка окна, «О программе», ошибок (например <c>0.5.3</c>).</summary>
    public static string DisplayVersion => _display.Value;

    /// <summary>Версия сборки — для сравнения с опубликованными обновлениями.</summary>
    public static Version AssemblyVersion => _assemblyVersion.Value;

    /// <summary>Та же нотация, что <see cref="DisplayVersion"/>, для произвольного <see cref="Version"/>.</summary>
    public static string FormatVersionUi(Version v) => $"{v.Major}.{v.Minor}.{v.Build}";

    private static string ReadDisplay()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "0.0.0" : FormatVersionUi(v);
    }

    private static Version ReadAssemblyVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v ?? new Version(0, 0, 0, 0);
    }
}
