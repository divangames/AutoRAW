using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoRAW.Services;

public readonly record struct WindowPanelsSnapshot(bool LogPanelVisible, bool ColorProfilePanelVisible, bool PreviewPanelVisible);

/// <summary>Сохранение состояния меню «Вид → Окна»: журнал, блок цветового профиля, блок превью.</summary>
public static class WindowPanelPreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class Root
    {
        public bool LogPanelVisible { get; set; }

        /// <summary>Null — по умолчанию блок скрыт.</summary>
        public bool? ColorProfilePanelVisible { get; set; }

        public bool? PreviewPanelVisible { get; set; }
    }

    private static string FilePath => AppPaths.WindowPanelPreferencesFile;

    private static Root Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new Root();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Root>(json, JsonOptions) ?? new Root();
        }
        catch
        {
            return new Root();
        }
    }

    private static void Save(Root root)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(root, JsonOptions));
    }

    public static WindowPanelsSnapshot GetSnapshot()
    {
        var r = Load();
        return new WindowPanelsSnapshot(
            r.LogPanelVisible,
            r.ColorProfilePanelVisible ?? false,
            r.PreviewPanelVisible ?? true);
    }

    public static bool GetLogPanelVisible() => Load().LogPanelVisible;

    public static bool GetColorProfilePanelVisible() => Load().ColorProfilePanelVisible ?? false;

    public static bool GetPreviewPanelVisible() => Load().PreviewPanelVisible ?? true;

    public static void SetLogPanelVisible(bool value)
    {
        var root = Load();
        root.LogPanelVisible = value;
        Save(root);
    }

    public static void SetColorProfilePanelVisible(bool value)
    {
        var root = Load();
        root.ColorProfilePanelVisible = value;
        Save(root);
    }

    public static void SetPreviewPanelVisible(bool value)
    {
        var root = Load();
        root.PreviewPanelVisible = value;
        Save(root);
    }
}
