namespace AutoRAW.Models;

public sealed class ColorRowDto
{
    public string?  XmpFilePath           { get; set; }
    public bool     UseStandardColorSpace { get; set; }
    public double   Contrast              { get; set; }
    public double   TemperatureKelvin     { get; set; }
    public double   Tint                  { get; set; }
    public double   Shadows               { get; set; }
    public double   Exposure              { get; set; }
    public double   Highlights            { get; set; }
    public double   Whites                { get; set; }
    public double   Blacks                { get; set; }
    public double   Vibrance              { get; set; }
    public double   Saturation            { get; set; }
    public double   Sharpness             { get; set; }
    public double   Clarity               { get; set; }
    public double   Dehaze                { get; set; }
}

/// <summary>Параметры цветокоррекции после кропа.</summary>
public sealed record ColorCorrectionSettings
{
    /// <summary>Путь к исходному XMP-пресету (для отображения и сохранения).</summary>
    public string? XmpFilePath { get; init; }

    public bool   UseStandardColorSpace { get; init; }

    // ── Тон / Баланс белого ──────────────────────────────────────────────
    public double TemperatureKelvin { get; init; }
    public double Tint              { get; init; }

    // ── Свет ─────────────────────────────────────────────────────────────
    public double Exposure   { get; init; }
    public double Contrast   { get; init; }
    public double Highlights { get; init; }
    public double Shadows    { get; init; }
    public double Whites     { get; init; }
    public double Blacks     { get; init; }

    // ── Чёткость / Деталь ────────────────────────────────────────────────
    public double Clarity  { get; init; }
    public double Dehaze   { get; init; }

    // ── Насыщенность ─────────────────────────────────────────────────────
    public double Vibrance   { get; init; }
    public double Saturation { get; init; }

    // ── Резкость ─────────────────────────────────────────────────────────
    /// <summary>Резкость 0-150 (как в Lightroom).</summary>
    public double Sharpness { get; init; }

    // ── Дефолты ──────────────────────────────────────────────────────────

    /// <summary>Используется как fallback если XMP-файл недоступен.</summary>
    public static ColorCorrectionSettings SneakersDefaults { get; } = new()
    {
        UseStandardColorSpace = true,
        TemperatureKelvin     = 6500,
        Tint                  = 4,
        Contrast              = 20,
        Sharpness             = 40,
    };

    public static ColorCorrectionSettings Neutral { get; } = new()
    {
        UseStandardColorSpace = false,
        TemperatureKelvin     = 6500,
        Sharpness             = 40,
    };

    // ── Сериализация ─────────────────────────────────────────────────────

    public static ColorCorrectionSettings FromDto(ColorRowDto? dto)
    {
        if (dto is null)
            return Neutral;

        return new ColorCorrectionSettings
        {
            XmpFilePath           = dto.XmpFilePath,
            UseStandardColorSpace = dto.UseStandardColorSpace,
            TemperatureKelvin     = dto.TemperatureKelvin,
            Tint                  = dto.Tint,
            Exposure              = dto.Exposure,
            Contrast              = dto.Contrast,
            Highlights            = dto.Highlights,
            Shadows               = dto.Shadows,
            Whites                = dto.Whites,
            Blacks                = dto.Blacks,
            Clarity               = dto.Clarity,
            Dehaze                = dto.Dehaze,
            Vibrance              = dto.Vibrance,
            Saturation            = dto.Saturation,
            Sharpness             = dto.Sharpness,
        };
    }

    public ColorRowDto ToDto() => new()
    {
        XmpFilePath           = XmpFilePath,
        UseStandardColorSpace = UseStandardColorSpace,
        TemperatureKelvin     = TemperatureKelvin,
        Tint                  = Tint,
        Exposure              = Exposure,
        Contrast              = Contrast,
        Highlights            = Highlights,
        Shadows               = Shadows,
        Whites                = Whites,
        Blacks                = Blacks,
        Clarity               = Clarity,
        Dehaze                = Dehaze,
        Vibrance              = Vibrance,
        Saturation            = Saturation,
        Sharpness             = Sharpness,
    };

    /// <summary>Возвращает копию (как <c>with { }</c>).</summary>
    public ColorCorrectionSettings Copy() => this with { };

    /// <summary>Краткая читаемая строка с ключевыми параметрами.</summary>
    public string ToSummaryString()
    {
        var parts = new List<string>();
        if (TemperatureKelvin != 6500 || Math.Abs(Tint) > 0.5)
            parts.Add($"Темп: {TemperatureKelvin:0} K, Отт: {Tint:+0;-0;0}");
        if (Math.Abs(Exposure) > 0.01)
            parts.Add($"Эксп: {Exposure:+0.00;-0.00;0.00}");
        if (Math.Abs(Contrast) > 0.5)
            parts.Add($"Контр: {Contrast:+0;-0;0}");
        if (Math.Abs(Highlights) > 0.5)
            parts.Add($"Света: {Highlights:+0;-0;0}");
        if (Math.Abs(Shadows) > 0.5)
            parts.Add($"Тени: {Shadows:+0;-0;0}");
        if (Math.Abs(Saturation) > 0.5)
            parts.Add($"Нас: {Saturation:+0;-0;0}");
        if (Math.Abs(Vibrance) > 0.5)
            parts.Add($"Вибр: {Vibrance:+0;-0;0}");
        if (Math.Abs(Sharpness - 40) > 0.5)
            parts.Add($"Рез: {Sharpness:0}");
        return parts.Count > 0 ? string.Join("; ", parts) : "Нейтральные настройки";
    }
}
