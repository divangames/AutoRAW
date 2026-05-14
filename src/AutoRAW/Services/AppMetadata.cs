using System.Reflection;

namespace AutoRAW.Services;

/// <summary>Версия продукта для UI и сплэша. Меняйте в одном месте: <c>AutoRAW.csproj</c> — свойства Version и InformationalVersion.</summary>
public static class AppMetadata
{
    private static readonly Lazy<string> _display = new(ReadDisplay);

    /// <summary>Строка вида «0.5.0 Alpha» из сборки (InformationalVersion).</summary>
    public static string DisplayVersion => _display.Value;

    private static string ReadDisplay()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
            return info.Trim();
        return asm.GetName().Version?.ToString() ?? "0.5.0.0";
    }
}
