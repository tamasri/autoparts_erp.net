using System.Globalization;
using Humanizer;

namespace AutoPartsERP.Application.Features.Reports;

internal static class ReportMappings
{
    private static readonly CultureInfo Arabic = new("ar");

    public static string PeriodDisplay(int year, int month) =>
        new DateTime(year, month, 1).ToString("MMMM yyyy", Arabic);

    public static string TimeAgo(DateTimeOffset createdAt) =>
        createdAt.Humanize(culture: Arabic);
}
