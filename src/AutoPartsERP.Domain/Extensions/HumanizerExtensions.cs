using System.Globalization;
using System.ComponentModel;
using Humanizer;

namespace AutoPartsERP.Domain.Extensions;

public static class HumanizerExtensions
{
    private static readonly CultureInfo Arabic = new("ar");

    public static string HumanizeAr(this DateTimeOffset dt) =>
        dt.Humanize(culture: Arabic);

    public static string HumanizeAr(this TimeSpan ts) =>
        ts.Humanize(culture: Arabic);

    public static string ToWordsAr(this long number) =>
        number.ToWords(Arabic);

    public static string ToWordsAr(this decimal amount) =>
        ((long)Math.Floor(amount)).ToWords(Arabic);

    public static string HumanizeAr(this Enum value) =>
        value
            .GetType()
            .GetField(value.ToString())
            ?.GetCustomAttributes(typeof(DescriptionAttribute), false)
            .OfType<DescriptionAttribute>()
            .FirstOrDefault()?.Description
        ?? value.ToString().Humanize();
}
