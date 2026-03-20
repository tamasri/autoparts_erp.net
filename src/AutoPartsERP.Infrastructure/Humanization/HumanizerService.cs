using AutoPartsERP.Domain.Extensions;
using System.Globalization;
using AutoPartsERP.Application.Common.Abstractions;
using Humanizer;

namespace AutoPartsERP.Infrastructure.Humanization;

public sealed class HumanizerService : IHumanizerService
{
    private static readonly CultureInfo Arabic = new("ar");

    public string HumanizeEnum(Enum value) => value.HumanizeAr();

    public string HumanizeDate(DateTimeOffset value) => value.Humanize(culture: Arabic);

    public string HumanizeTimeSpan(TimeSpan value) => value.Humanize(culture: Arabic);

    public string AmountToWords(decimal amount, string currencyName) => AmountInWordsService.ToArabicWords(amount, currencyName);

    public string FormatPeriod(int year, int month)
    {
        var normalizedMonth = Math.Clamp(month, 1, 12);
        return new DateTime(year, normalizedMonth, 1).ToString("MMMM yyyy", Arabic);
    }
}
