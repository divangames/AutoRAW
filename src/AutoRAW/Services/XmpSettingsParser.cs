using System.IO;
using System.Xml.Linq;
using AutoRAW.Models;

namespace AutoRAW.Services;

/// <summary>Разбирает Camera Raw XMP-пресет и возвращает <see cref="ColorCorrectionSettings"/>.</summary>
public static class XmpSettingsParser
{
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Crs = "http://ns.adobe.com/camera-raw-settings/1.0/";

    public static ColorCorrectionSettings Parse(string path)
    {
        var doc = XDocument.Load(path);

        var desc = doc.Descendants(Rdf + "Description").FirstOrDefault()
                   ?? throw new InvalidDataException("Не найден rdf:Description в XMP-файле.");

        double Attr(string name, double fallback)
        {
            var val = desc.Attribute(Crs + name)?.Value;
            if (val is null) return fallback;
            val = val.Trim().TrimStart('+');
            return double.TryParse(val, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        return new ColorCorrectionSettings
        {
            XmpFilePath     = path,
            UseStandardColorSpace = true,
            TemperatureKelvin = Attr("Temperature",   6500),
            Tint            = Attr("Tint",            0),
            Exposure        = Attr("Exposure2012",    0),
            Contrast        = Attr("Contrast2012",    0),
            Highlights      = Attr("Highlights2012",  0),
            Shadows         = Attr("Shadows2012",     0),
            Whites          = Attr("Whites2012",      0),
            Blacks          = Attr("Blacks2012",      0),
            Clarity         = Attr("Clarity2012",     0),
            Dehaze          = Attr("Dehaze",          0),
            Vibrance        = Attr("Vibrance",        0),
            Saturation      = Attr("Saturation",      0),
            Sharpness       = Attr("Sharpness",       40),
        };
    }
}
