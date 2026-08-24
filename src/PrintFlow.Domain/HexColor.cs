using System.Globalization;

namespace PrintFlow.Domain;

/// <summary>
/// بيحوّل لون مكتوب بصيغة hex (زي "#1B2A4A") لمكوناته.
/// بيرجّع بايتات مجردة عشان الـ Domain مايعتمدش على أي مكتبة رسم.
/// </summary>
public static class HexColor
{
    public static readonly RgbColor Black = new(0, 0, 0);

    public static bool TryParse(string? hex, out RgbColor color)
    {
        color = Black;

        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        string value = hex.Trim().TrimStart('#');

        // صيغة مختصرة زي #C30 معناها #CC3300
        if (value.Length == 3)
        {
            value = string.Concat(value.Select(c => new string(c, 2)));
        }

        if (value.Length != 6 || !value.All(Uri.IsHexDigit))
        {
            return false;
        }

        color = new RgbColor(
            byte.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

        return true;
    }

    /// <summary>بيحوّل، ولو الصيغة غلط بيرجّع اللون البديل بدل ما يرمي استثناء وسط الطباعة.</summary>
    public static RgbColor ParseOrDefault(string? hex, RgbColor fallback) =>
        TryParse(hex, out var color) ? color : fallback;
}

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>
    /// سطوع اللون بمعادلة الإدراك البشري (العين بتحس بالأخضر أكتر من الأزرق).
    /// 0 = أسود تمام، 1 = أبيض تمام.
    /// </summary>
    public double Luminance => (0.299 * R + 0.587 * G + 0.114 * B) / 255.0;

    /// <summary>اللون فاتح ولا غامق — اللي بيحدد لون اللوحة اللي وراه.</summary>
    public bool IsLight => Luminance >= 0.5;
}
