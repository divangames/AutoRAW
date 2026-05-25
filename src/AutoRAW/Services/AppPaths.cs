using System.IO;
using AutoRAW.Models;

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

    /// <summary>Корень папки reference рядом с exe (без подкаталога профиля).</summary>
    public static string DefaultReferenceFolder => Path.Combine(AppRoot, "reference");

    /// <summary>Корень папки zona рядом с exe (без подкаталога профиля).</summary>
    public static string DefaultZonaFolder => Path.Combine(AppRoot, "zona");

    /// <summary>Подкаталог встроенного профиля «Кроссовки» внутри <c>reference\</c> и <c>zona\</c>.</summary>
    public const string BuiltInSneakersSubfolder = "Sneakers";

    /// <summary><c>…\reference\Sneakers</c> рядом с программой.</summary>
    public static string BuiltInSneakersReferenceFolder =>
        Path.Combine(AppRoot, "reference", BuiltInSneakersSubfolder);

    /// <summary><c>…\zona\Sneakers</c> рядом с программой.</summary>
    public static string BuiltInSneakersZonaFolder =>
        Path.Combine(AppRoot, "zona", BuiltInSneakersSubfolder);

    /// <summary>Каталог манифестов встроенных профилей: <c>profiles\&lt;slug&gt;\profile.json</c> рядом с exe.</summary>
    public static string ShippedProfilesCatalogRoot => Path.Combine(AppRoot, "profiles");

    /// <summary>Стартовые ручные правки по номеру кадра для «Кроссовки» из комплекта (не перезаписывают <c>%AppData%\ manual_shot_adjust.json</c>).</summary>
    public static string BuiltInSneakersManualShotDefaultsFile =>
        Path.Combine(ShippedProfilesCatalogRoot, BuiltInSneakersSubfolder, "manual_shot_profile_defaults.json");

    /// <summary>Тот же слот «Кроссовки» (папки <c>reference\Sneakers</c> и <c>zona\Sneakers</c>), в т.ч. если профиль загружен из <c>profiles\Sneakers</c>.</summary>
    public static bool ReferencesBuiltInSneakersFolders(ProductProfile? p)
    {
        if (p is null || string.IsNullOrWhiteSpace(p.ReferenceFolder) || string.IsNullOrWhiteSpace(p.ZonaFolder))
            return false;
        try
        {
            var r = Path.GetFullPath(p.ReferenceFolder.Trim());
            var z = Path.GetFullPath(p.ZonaFolder.Trim());
            return string.Equals(r, Path.GetFullPath(BuiltInSneakersReferenceFolder), StringComparison.OrdinalIgnoreCase)
                && string.Equals(z, Path.GetFullPath(BuiltInSneakersZonaFolder), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Профиль из %LocalAppData% (можно перезаписать через «Сохранить в профиль»).</summary>
    public static bool IsUserInstallProfile(ProductProfile p)
    {
        if (p.IsDraft || string.IsNullOrWhiteSpace(p.ReferenceFolder))
            return false;
        try
        {
            var r = Path.GetFullPath(p.ReferenceFolder.Trim());
            var root = Path.GetFullPath(UserProfilesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return r.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r, root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Папка с экшен-дроплетами Photoshop рядом с программой (<c>droplets\01_drop.exe</c> и т.д.).</summary>
    public static string DropletsFolder => Path.Combine(AppRoot, "droplets");

    /// <summary>Каталог ONNX-моделей детекции товара (<c>models\subject\yolov8n.onnx</c>).</summary>
    public static string SubjectModelsFolder => Path.Combine(AppRoot, "models", "subject");

    public static string SubjectYoloV8ModelFile =>
        ResolveSubjectOnnxPath("yolov8n.onnx") ?? Path.Combine(SubjectModelsFolder, "yolov8n.onnx");

    public static string SubjectYoloV8SegModelFile =>
        ResolveSubjectOnnxPath("yolov8n-seg.onnx") ?? Path.Combine(SubjectModelsFolder, "yolov8n-seg.onnx");

    public static string SubjectU2NetpModelFile =>
        ResolveSubjectOnnxPath("u2netp.onnx") ?? Path.Combine(SubjectModelsFolder, "u2netp.onnx");

    /// <summary>Ищет ONNX рядом с exe и вверх по дереву (dev: корень репозитория).</summary>
    public static string? ResolveSubjectOnnxPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var direct = Path.Combine(SubjectModelsFolder, fileName);
        if (File.Exists(direct))
            return Path.GetFullPath(direct);

        var dir = AppRoot;
        for (var i = 0; i < 10; i++)
        {
            var probe = Path.Combine(dir, "models", "subject", fileName);
            if (File.Exists(probe))
                return Path.GetFullPath(probe);

            var parent = Directory.GetParent(dir)?.FullName;
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                break;
            dir = parent;
        }

        return null;
    }

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

    /// <summary>Ручные правки кадра (%AppData%\AutoRAW\manual_shot_adjust.json).</summary>
    public static string ManualShotAdjustStoreFile => Path.Combine(RoamingRoot, "manual_shot_adjust.json");

    /// <summary>Выбранный эталон в очереди кадрирования по полному пути входа (%AppData%\AutoRAW\crop_frame_refs.json).</summary>
    public static string CropFrameReferenceChoicesFile => Path.Combine(RoamingRoot, "crop_frame_refs.json");

    /// <summary>Файл пользовательских товаров (старый формат, до миграции).</summary>
    public static string CustomProductsFile => Path.Combine(RoamingRoot, "products.json");

    public static string ProfilePreferencesFile => Path.Combine(RoamingRoot, "profile_prefs.json");

    public static string ExportPreferencesFile => Path.Combine(RoamingRoot, "export_prefs.json");

    /// <summary>Загрузчик RAW: LibRaw или ImageMagick (%AppData%\AutoRAW\raw_loader_prefs.json).</summary>
    public static string RawLoaderPreferencesFile => Path.Combine(RoamingRoot, "raw_loader_prefs.json");

    public static string AlignQualityPreferencesFile => Path.Combine(RoamingRoot, "align_quality_prefs.json");

    /// <summary>Видимость панелей из меню «Вид → Окна» (журнал, цветовой профиль, превью).</summary>
    public static string WindowPanelPreferencesFile => Path.Combine(RoamingRoot, "window_panels.json");

    /// <summary>Тема интерфейса: «Вид → Тема».</summary>
    public static string ThemePreferencesFile => Path.Combine(RoamingRoot, "theme_prefs.json");

    /// <summary>Фотограф для сопоставления съёмки: «Профиль → Фотограф».</summary>
    public static string PhotographerPreferencesFile => Path.Combine(RoamingRoot, "photographer_prefs.json");

    public static string TelegramZonaPreferencesFile => Path.Combine(RoamingRoot, "telegram_zona.json");

    /// <summary>Перед запуском установщика обновления — текст «что нового» для показа после установки.</summary>
    public static string PendingReleaseNotesFile => Path.Combine(RoamingRoot, "pending_release_notes.json");

    public static string ProfileColorOverridesFile => Path.Combine(RoamingRoot, "profile_colors.json");

    // ── Вспомогательные ────────────────────────────────────────────────────────

    public static string ResolveReferenceFolder(string? profileOverride)
    {
        if (!string.IsNullOrWhiteSpace(profileOverride))
            return Path.GetFullPath(profileOverride.Trim());
        return DefaultReferenceFolder;
    }

    public static string ResolveZonaFolder(string? profileOverride)
    {
        if (!string.IsNullOrWhiteSpace(profileOverride))
            return Path.GetFullPath(profileOverride.Trim());
        return DefaultZonaFolder;
    }
}
