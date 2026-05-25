using System.Collections.Concurrent;
using AutoRAW.Models;
using AutoRAW.Services.SubjectDetection;
using ImageMagick;

namespace AutoRAW.Services;

/// <summary>
/// Анализ и кэш шаблонов композиции из папки <c>reference\</c> (без zona/operation).
/// </summary>
public static class ReferenceCompositionCatalog
{
    private const int TemplateBuildVersion = 3;

    private sealed class CacheEntry(ReferenceCompositionTemplate Template, DateTime FileTimeUtc, int BuildVersion)
    {
        public ReferenceCompositionTemplate Template { get; } = Template;
        public DateTime FileTimeUtc { get; } = FileTimeUtc;
        public int BuildVersion { get; } = BuildVersion;
    }

    private static readonly ConcurrentDictionary<string, CacheEntry> ByPath = new(StringComparer.OrdinalIgnoreCase);

    public static ReferenceCompositionTemplate GetOrBuild(string referencePath, int analysisMaxEdge)
    {
        var path = Path.GetFullPath(referencePath);
        var mtime = File.GetLastWriteTimeUtc(path);
        if (ByPath.TryGetValue(path, out var cached) && cached.FileTimeUtc == mtime
            && cached.BuildVersion == TemplateBuildVersion)
            return cached.Template;

        using var img = RasterImageLoader.Load(path);
        AutoCropComputation.AutoOrientAndNormalize(img);
        var template = BuildFromImage(path, img, analysisMaxEdge);
        ByPath[path] = new CacheEntry(template, mtime, TemplateBuildVersion);
        return template;
    }

    public static IReadOnlyDictionary<string, ReferenceCompositionTemplate> BuildFolder(
        string referenceFolder,
        int analysisMaxEdge)
    {
        var dict = new Dictionary<string, ReferenceCompositionTemplate>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(referenceFolder))
            return dict;

        foreach (var file in ImageFileCatalog.ListImagesInFolder(referenceFolder))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(stem))
                continue;
            dict[stem] = GetOrBuild(file, analysisMaxEdge);
        }

        return dict;
    }

    public static void InvalidateFolder(string referenceFolder)
    {
        if (!Directory.Exists(referenceFolder))
            return;

        var root = Path.GetFullPath(referenceFolder);
        foreach (var key in ByPath.Keys.ToList())
        {
            if (key.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, root, StringComparison.OrdinalIgnoreCase))
            {
                ByPath.TryRemove(key, out _);
            }
        }
    }

    private static ReferenceCompositionTemplate BuildFromImage(
        string referencePath,
        MagickImage refImage,
        int analysisMaxEdge)
    {
        var refW = (double)refImage.Width;
        var refH = (double)refImage.Height;
        var stem = Path.GetFileNameWithoutExtension(referencePath) ?? string.Empty;

        using var analysis = AutoCropComputation.CloneResizedLongEdge(refImage, analysisMaxEdge);
        var scale = refW / analysis.Width;

        SubjectDetectionResult det;
        Box2d subject;
        using (var mat = MagickMatConverter.ToMatBgr(analysis))
        {
            det = SubjectDetectionService.DetectOnReferenceMat(mat);
            subject = det.Subject;
        }

        subject = subject.Scale(scale, scale);

        return new ReferenceCompositionTemplate
        {
            Stem = stem,
            FilePath = referencePath,
            RefW = refW,
            RefH = refH,
            SubjectBox = subject,
            DetectSource = det.Source
        };
    }
}
