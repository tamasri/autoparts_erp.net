using System.Globalization;
using Humanizer;

namespace AutoPartsERP.Application.Features.Printing;

/// <summary>How the printed forms write amounts, dates and sums in words.</summary>
public static class PrintFormat
{
    public const string Syp = "ل.س";
    public const string Usd = "$";

    private static readonly CultureInfo Arabic = new("ar");

    /// <summary>Two decimals, thousands separated: 1,234.50; a negative in brackets, (32.00) — a leading minus flips to the end in Arabic text.</summary>
    public static string Money(decimal value) =>
        value < 0 ? $"({(-value).ToString("N2", CultureInfo.InvariantCulture)})" : value.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>A number for a money column (the renderer formats it): 1234.5.</summary>
    public static string Raw(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Amount(decimal value, string currency) => $"{Money(value)} {currency}";

    public static string Date(DateOnly date) => date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    public static string DayFirst(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>"فقط ثلاثمائة واثنان وخمسون ليرة سورية وأربعون قرشاً لا غير".</summary>
    public static string SypInWords(decimal amount) => InWords(amount, "ليرة سورية", "قرشاً");

    /// <summary>"فقط خمسون دولاراً أمريكياً وخمسة وعشرون سنتاً لا غير".</summary>
    public static string UsdInWords(decimal amount) => InWords(amount, "دولاراً أمريكياً", "سنتاً");

    private static string InWords(decimal amount, string unit, string cents)
    {
        var value = Math.Abs(amount);
        var whole = (long)Math.Floor(value);
        var fraction = (int)Math.Round((value - whole) * 100, MidpointRounding.AwayFromZero);
        var words = whole == 0 ? "صفر" : Tidy(whole.ToWords(Arabic));
        var fractionWords = fraction > 0 ? $" و{Tidy(fraction.ToWords(Arabic))} {cents}" : string.Empty;
        return $"فقط {words} {unit}{fractionWords} لا غير";
    }

    /// <summary>Humanizer writes "ثلاث مئة و اثنان"; the written form is "ثلاثمائة واثنان".</summary>
    private static string Tidy(string words)
    {
        var joined = Regex.Replace(words, @"(^|\s)و\s+", "$1و");
        joined = Regex.Replace(joined, @"(ثلاث|أربع|خمس|ست|سبع|ثمان|تسع)\s+مئة", "$1مائة");
        return joined.Replace("مئة", "مائة").Replace("مئتان", "مائتان");
    }
}
