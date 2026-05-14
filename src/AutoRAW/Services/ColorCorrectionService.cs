using AutoRAW.Models;
using ImageMagick;

namespace AutoRAW.Services;

/// <summary>Цветокоррекция после кропа (Magick.NET), применяет все поля <see cref="ColorCorrectionSettings"/>.</summary>
public static class ColorCorrectionService
{
    public static void ApplyIfEnabled(MagickImage image, ColorCorrectionSettings settings, bool enabled)
    {
        if (!enabled)
            return;

        if (settings.UseStandardColorSpace)
        {
            image.ColorSpace = ColorSpace.sRGB;
            image.Strip();
        }

        // Баланс белого
        ApplyTemperatureAndTint(image, settings.TemperatureKelvin, settings.Tint);

        // Экспозиция: EV шаг = ×2^ev
        if (Math.Abs(settings.Exposure) > 0.005)
        {
            var mul = Math.Pow(2.0, settings.Exposure);
            image.Evaluate(Channels.RGB, EvaluateOperator.Multiply, mul);
        }

        // Контраст
        if (Math.Abs(settings.Contrast) > 0.5)
            image.BrightnessContrast(new Percentage(0), new Percentage(Math.Clamp(settings.Contrast, -100, 100)));

        // Света / Тени / Белые / Чёрные — через уровни/кривые
        ApplyTonalAdjustments(image, settings.Highlights, settings.Shadows, settings.Whites, settings.Blacks);

        // Насыщенность + Вибранс
        ApplySaturationVibrance(image, settings.Saturation, settings.Vibrance);

        // Резкость (UnsharpMask, как в LR: Sharpness 0-150 → amount 0..3.0)
        if (settings.Sharpness > 1)
        {
            var amount = settings.Sharpness / 50.0;   // 40 → 0.8 (типичный LR default)
            image.UnsharpMask(0.5, 0.5, amount, 0.05);
        }

        // Clarity — локальный контраст (широкий unsharp)
        if (Math.Abs(settings.Clarity) > 1)
        {
            var ca = settings.Clarity / 100.0 * 0.6;
            image.UnsharpMask(10.0, 6.0, ca, 0.0);
        }

        // Dehaze — примерно поднимает контраст + слегка насыщает
        if (Math.Abs(settings.Dehaze) > 1)
        {
            var da = settings.Dehaze / 200.0;
            image.BrightnessContrast(new Percentage(0), new Percentage(da * 20));
        }
    }

    // ── Баланс белого ────────────────────────────────────────────────────

    private static void ApplyTemperatureAndTint(MagickImage img, double tempK, double tint)
    {
        const double neutralK = 6500;
        var dt = (tempK - neutralK) / neutralK;
        var rGain = 1.0 + dt * 0.18;
        var bGain = 1.0 - dt * 0.14;
        var tm = tint / 100.0;
        var gGain = 1.0 - tm * 0.03;
        rGain += tm * 0.012;
        bGain += tm * 0.012;

        img.Evaluate(Channels.Red,   EvaluateOperator.Multiply, rGain);
        img.Evaluate(Channels.Green, EvaluateOperator.Multiply, gGain);
        img.Evaluate(Channels.Blue,  EvaluateOperator.Multiply, bGain);
    }

    // ── Тональная коррекция ──────────────────────────────────────────────

    private static void ApplyTonalAdjustments(MagickImage img,
        double highlights, double shadows, double whites, double blacks)
    {
        // Тени — подъём (lift) нижней части гистограммы
        if (Math.Abs(shadows) > 0.5)
        {
            var s = Math.Clamp(shadows, -100, 100);
            // +100 → ~+22% яркости для тёмных тонов
            img.BrightnessContrast(new Percentage(s * 0.22), new Percentage(0));
        }

        // Света — уменьшение/увеличение ярких тонов через Gamma на верхней части
        if (Math.Abs(highlights) > 0.5)
        {
            // highlights > 0: восстанавливаем пересветы → gamma < 1 для светлых пикселей
            // Простая аппроксимация: level с небольшим смещением белой точки
            var hl = Math.Clamp(highlights, -100, 100);
            var whitePct = 100.0 - hl * 0.15;   // +100 → white point = 85
            whitePct = Math.Clamp(whitePct, 50, 130);
            img.Level(new Percentage(0), new Percentage(whitePct));
        }

        // Белые (Whites) — смещение абсолютной белой точки
        if (Math.Abs(whites) > 0.5)
        {
            var w = Math.Clamp(whites, -100, 100);
            img.Level(new Percentage(0), new Percentage(100 - w * 0.1));
        }

        // Чёрные (Blacks) — смещение абсолютной чёрной точки
        if (Math.Abs(blacks) > 0.5)
        {
            var b = Math.Clamp(blacks, -100, 100);
            img.Level(new Percentage(b * 0.08), new Percentage(100));
        }
    }

    // ── Насыщенность ────────────────────────────────────────────────────

    private static void ApplySaturationVibrance(MagickImage img, double saturation, double vibrance)
    {
        var combined = saturation + vibrance * 0.5;
        if (Math.Abs(combined) < 0.5)
            return;

        // Modulate: Saturation % = 100 + delta%  (100 = нейтрально)
        var sat = 100.0 + Math.Clamp(combined, -100, 150);
        img.Modulate(new Percentage(100), new Percentage(sat), new Percentage(100));
    }
}
