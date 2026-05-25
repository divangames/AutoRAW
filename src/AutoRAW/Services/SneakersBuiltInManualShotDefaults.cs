using System.Text.Json;
using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>
/// Стартовые ручные правки по номеру кадра для штатного профиля из
/// <c>profiles\Sneakers\manual_shot_profile_defaults.json</c>.
/// Записи в <see cref="ManualShotAdjustStore"/> (per-file и profileDefaults в %AppData%) имеют приоритет.
/// </summary>
public static class SneakersBuiltInManualShotDefaults
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private sealed class FileDto
    {
        public string DisplayName { get; set; } = string.Empty;

        public Dictionary<string, StemDto>? StemDefaults { get; set; }
    }

    private sealed class StemDto
    {
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double ZoomPercent { get; set; } = 100;
        public double RotationDeg { get; set; }
        public ZonaGridOverlayKind GridOverlay { get; set; }

        public ManualShotAdjust ToModel() => new()
        {
            OffsetX = OffsetX,
            OffsetY = OffsetY,
            ZoomPercent = ZoomPercent,
            RotationDeg = RotationDeg,
            GridOverlay = GridOverlay
        };
    }

    private static bool _loaded;
    private static string? _profileDisplayName;
    private static Dictionary<string, ManualShotAdjust>? _byStem;

    /// <summary>Возвращает встроенные смещение/масштаб, если они заданы в json для этого профиля и стема.</summary>
    public static bool TryGetAdjust(string? profileDisplayName, string shotStemRaw, out ManualShotAdjust adjust)
    {
        adjust = new ManualShotAdjust();
        if (string.IsNullOrWhiteSpace(profileDisplayName) || string.IsNullOrWhiteSpace(shotStemRaw))
            return false;

        lock (Gate)
        {
            EnsureLoaded_Unlocked();
            if (_byStem is null || string.IsNullOrEmpty(_profileDisplayName))
                return false;

            if (!string.Equals(profileDisplayName.Trim(), _profileDisplayName, StringComparison.OrdinalIgnoreCase))
                return false;

            return TryResolveStem_Unlocked(_byStem, shotStemRaw.Trim(), out adjust);
        }
    }

    private static void EnsureLoaded_Unlocked()
    {
        if (_loaded)
            return;

        _loaded = true;
        try
        {
            var path = AppPaths.BuiltInSneakersManualShotDefaultsFile;
            if (!File.Exists(path))
                return;

            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<FileDto>(json, Json);
            if (dto is null || string.IsNullOrWhiteSpace(dto.DisplayName) || dto.StemDefaults is null || dto.StemDefaults.Count == 0)
                return;

            var map = new Dictionary<string, ManualShotAdjust>(StringComparer.OrdinalIgnoreCase);
            foreach (var (stemKey, stemDto) in dto.StemDefaults)
            {
                if (string.IsNullOrWhiteSpace(stemKey) || stemDto is null)
                    continue;

                var norm = NormalizeStemKey(stemKey);
                map[norm] = stemDto.ToModel();
            }

            if (map.Count > 0)
            {
                _profileDisplayName = dto.DisplayName.Trim();
                _byStem = map;
            }
        }
        catch
        {
            /* остаёмся без встроенных умолчаний */
        }
    }

    /// <summary>«5»→«05» для ключей вида номера кадра.</summary>
    private static string NormalizeStemKey(string raw)
    {
        var s = raw.Trim();
        if (string.IsNullOrEmpty(s))
            return s;

        var stem = ZonaOperationGuideParser.NormalizeShotStem(s, null);
        return string.IsNullOrEmpty(stem) ? s : stem;
    }

    private static bool TryResolveStem_Unlocked(
        Dictionary<string, ManualShotAdjust> map,
        string stemRaw,
        out ManualShotAdjust adjust)
    {
        adjust = new ManualShotAdjust();
        var k = NormalizeStemKey(stemRaw);
        if (map.TryGetValue(k, out adjust))
            return true;

        if (!string.Equals(k, stemRaw, StringComparison.OrdinalIgnoreCase)
            && map.TryGetValue(stemRaw, out adjust))
            return true;

        return false;
    }
}
