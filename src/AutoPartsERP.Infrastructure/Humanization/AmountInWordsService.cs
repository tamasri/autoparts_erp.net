using System.Globalization;
using Humanizer;

namespace AutoPartsERP.Infrastructure.Humanization;

public static class AmountInWordsService
{
    private static readonly CultureInfo Arabic = new("ar");

    public static string ToArabicWords(decimal amount, string currencyName)
    {
        var whole = (long)Math.Floor(Math.Abs(amount));
        var fraction = (int)Math.Round((Math.Abs(amount) - whole) * 100);
        var sign = amount < 0 ? "سالب " : string.Empty;

        var wholeWords = whole.ToWords(Arabic);
        var fractionPart = fraction > 0
            ? $" و{fraction.ToWords(Arabic)} قرش"
            : string.Empty;

        return $"{sign}{wholeWords} {currencyName}{fractionPart} فقط لا غير";
    }
}
