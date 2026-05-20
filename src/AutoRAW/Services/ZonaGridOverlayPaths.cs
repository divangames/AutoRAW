using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>Пути к PNG сетки в папке профиля zona.</summary>
public static class ZonaGridOverlayPaths
{
    public static string? TryResolve(string? zonaProfileFolder, ZonaGridOverlayKind kind)
    {
        if (string.IsNullOrWhiteSpace(zonaProfileFolder) || kind == ZonaGridOverlayKind.None)
            return null;

        var root = Path.GetFullPath(zonaProfileFolder.Trim());
        var name = kind switch
        {
            ZonaGridOverlayKind.Photo01 => "zona_tovara_01.png",
            ZonaGridOverlayKind.OtherPhotos => "zona_tovara_02.png",
            _ => null
        };
        if (name is not null)
        {
            var p = Path.Combine(root, name);
            if (File.Exists(p))
                return p;
        }

        var legacy = Path.Combine(root, "zona_tovara.png");
        return File.Exists(legacy) ? legacy : null;
    }
}
