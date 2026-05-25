using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>Совместимость: сетка рисуется в <see cref="SneakersLayoutSafeZone"/>, PNG больше не используются.</summary>
public static class ZonaGridOverlayPaths
{
    public static string? TryResolve(string? zonaProfileFolder, ZonaGridOverlayKind kind) => null;
}
