using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoRAW.Services;

/// <summary>Сохраняет текст релиза перед запуском установщика; после установки показывается один раз и файл удаляется.</summary>
public static class PendingReleaseNotesStore
{
    private sealed class Dto
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;
    }

    private const int MaxBodyChars = 120_000;

    public static void WritePending(string versionLabel, string body)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.PendingReleaseNotesFile)!);
        var b = body ?? string.Empty;
        if (b.Length > MaxBodyChars)
            b = b[..MaxBodyChars] + "\n\n…";
        var dto = new Dto { Version = versionLabel, Body = b };
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = false });
        File.WriteAllText(AppPaths.PendingReleaseNotesFile, json);
    }

    public static bool TryTake(out string versionLabel, out string body)
    {
        versionLabel = string.Empty;
        body = string.Empty;
        var path = AppPaths.PendingReleaseNotesFile;
        if (!File.Exists(path))
            return false;
        try
        {
            var json = File.ReadAllText(path);
            File.Delete(path);
            var dto = JsonSerializer.Deserialize<Dto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto is null)
                return false;
            versionLabel = dto.Version ?? string.Empty;
            body = dto.Body ?? string.Empty;
            return true;
        }
        catch
        {
            try { File.Delete(path); } catch { /* ignore */ }
            return false;
        }
    }
}
