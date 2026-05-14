using System.IO;

namespace AutoRAW.Services;

/// <summary>Пути приложения: ресурсы рядом с exe и пользовательские данные в LocalAppData.</summary>
public static class AppPaths
{
    /// <summary>Каталог, где лежит AutoRAW.dll / exe (обычно bin\…\net8.0-windows или C:\Program Files\AutoRAW).</summary>
    public static string AppRoot { get; } =
        Path.GetFullPath(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    // ── Ресурсы рядом с exe (только для чтения) ────────────────────────────────

    /// <summary>Папка с XMP-пресетами, поставляемыми вместе с приложением.</summary>
    public static string DefaultSettingFolder => Path.Combine(AppRoot, "setting");

    /// <summary>XMP-пресет для встроенного профиля «Кроссовки».</summary>
    public static string DefaultSneakersXmp => Path.Combine(DefaultSettingFolder, "01.xmp");

    public static string DefaultReferenceFolder => Path.Combine(AppRoot, "reference");

    public static string DefaultZonaFolder => Path.Combine(AppRoot, "zona");

    public static string InstructionFile => Path.Combine(AppRoot, "Instruction.md");

    public static string ChangelogFile => Path.Combine(AppRoot, "CHANGELOG.md");

    // ── Пользовательские данные — LocalAppData\AutoRAW (запись разрешена) ──────
    // Program Files защищён от записи без прав администратора, поэтому
    // профили, настройки и json-файлы хранятся в LocalAppData.

    private static string LocalDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoRAW");

    /// <summary>Корень пользовательских данных: %LocalAppData%\AutoRAW\user files</summary>
    public static string UserFilesRoot => Path.Combine(LocalDataRoot, "user files");

    /// <summary>Профили пользователя: %LocalAppData%\AutoRAW\user files\Profile</summary>
    public static string UserProfilesRoot => Path.Combine(UserFilesRoot, "Profile");

    // ── Файлы настроек (%AppData%) ──────────────────────────────────────────────

    private static string RoamingRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoRAW");

    /// <summary>Файл пользовательских товаров (старый формат, до миграции).</summary>
    public static string CustomProductsFile => Path.Combine(RoamingRoot, "products.json");

    public static string ProfilePreferencesFile => Path.Combine(RoamingRoot, "profile_prefs.json");

    public static string ExportPreferencesFile => Path.Combine(RoamingRoot, "export_prefs.json");

    public static string ProfileColorOverridesFile => Path.Combine(RoamingRoot, "profile_colors.json");

    // ── Вспомогательные ────────────────────────────────────────────────────────

    public static string ResolveReferenceFolder(string? profileOverride)
    {
        if (!string.IsNullOrWhiteSpace(profileOverride) && Directory.Exists(profileOverride))
            return Path.GetFullPath(profileOverride);
        return DefaultReferenceFolder;
    }

    public static string ResolveZonaFolder(string? profileOverride)
    {
        if (!string.IsNullOrWhiteSpace(profileOverride) && Directory.Exists(profileOverride))
            return Path.GetFullPath(profileOverride);
        return DefaultZonaFolder;
    }
}
