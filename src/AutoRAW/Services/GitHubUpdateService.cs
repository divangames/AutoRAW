using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoRAW.Services;

/// <summary>Последнее опубликованное обновление и установщик <c>AutoRAW-Setup-*-ru.exe</c> из вложений.</summary>
public sealed record GitHubReleaseOffer(
    ProductVersion Version,
    string TagLabel,
    string ReleaseTitle,
    string BodyMarkdown,
    string DownloadUrl,
    string AssetFileName);

public static class GitHubUpdateService
{
    private static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/divangames/AutoRAW/releases/latest");

    private static readonly HttpClient Http = CreateClient();

    private static readonly JsonSerializerOptions JsonRead = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"AutoRAW/{AppMetadata.DisplayVersion}");
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>Сравнение с локальной сборкой: вернуть предложение, только если опубликованная версия выше.</summary>
    public static async Task<GitHubReleaseOffer?> TryGetLatestOfferNewerThanAsync(ProductVersion current, CancellationToken ct = default)
    {
        var offer = await TryGetLatestOfferAsync(ct).ConfigureAwait(false);
        if (offer is null)
            return null;
        return offer.Version > current ? offer : null;
    }

    /// <summary>Последний релиз (даже если не новее текущей) — для ручной проверки.</summary>
    public static async Task<GitHubReleaseOffer?> TryGetLatestOfferAsync(CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync(LatestReleaseUri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(stream, JsonRead, ct).ConfigureAwait(false);
        if (dto is null)
            return null;
        var parsedVersion = ParseProductVersionFromTag(dto.TagName);
        if (!parsedVersion.HasValue)
            return null;
        var v = parsedVersion.Value;
        var asset = PickSetupAsset(dto);
        if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) || string.IsNullOrWhiteSpace(asset.Name))
            return null;
        return new GitHubReleaseOffer(
            v,
            dto.TagName?.Trim() ?? v.ToString(),
            (dto.Name ?? dto.TagName ?? v.ToString()).Trim(),
            dto.Body ?? string.Empty,
            asset.BrowserDownloadUrl.Trim(),
            asset.Name.Trim());
    }

    public static async Task DownloadToFileAsync(
        string downloadUrl,
        string destinationPath,
        IProgress<(long BytesReceived, long? TotalBytes)>? progress,
        CancellationToken ct)
    {
        using var resp = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength is > 0 ? resp.Content.Headers.ContentLength : (long?)null;
        await using var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var dir = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            read += n;
            progress?.Report((read, total));
        }
    }

    private static GitHubAssetDto? PickSetupAsset(GitHubReleaseDto rel)
    {
        if (rel.Assets is null || rel.Assets.Count == 0)
            return null;
        foreach (var a in rel.Assets)
        {
            if (a.Name is null)
                continue;
            if (a.Name.StartsWith("AutoRAW-Setup-", StringComparison.OrdinalIgnoreCase)
                && a.Name.EndsWith("-ru.exe", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        foreach (var a in rel.Assets)
        {
            if (a.Name is null)
                continue;
            if (a.Name.Contains("AutoRAW-Setup", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        return null;
    }

    private static ProductVersion? ParseProductVersionFromTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        var s = tag.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
            s = s[1..];
        var dash = s.IndexOfAny(['-', '+', ' ']);
        if (dash > 0)
            s = s[..dash];
        if (!ProductVersion.TryParse(s, out var v))
            return null;
        return v;
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAssetDto>? Assets { get; set; }
    }

    private sealed class GitHubAssetDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
