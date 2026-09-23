using AutoPartsERP.Contracts.Dashboard;

namespace AutoPartsERP.Application.Features.Dashboard;

/// <summary>The small formulas behind the KPI cards, kept out of SQL so they are tested and read the same everywhere.</summary>
public static class KpiMath
{
    /// <summary>Gross profit as a share of net sales; none when there were no net sales.</summary>
    public static decimal? MarginPct(decimal netSales, decimal grossProfit) => netSales <= 0 ? null : Math.Round(grossProfit / netSales * 100m, 1);

    /// <summary>Days sales outstanding: what customers owe, in days of the period's average daily net sales.</summary>
    public static decimal? Dso(decimal receivables, decimal netSales, DateOnly from, DateOnly to)
    {
        var days = to.DayNumber - from.DayNumber + 1;
        return netSales <= 0 || days <= 0 ? null : Math.Round(receivables / (netSales / days), 1);
    }

    public static decimal? Average(decimal total, int count) => count == 0 ? null : Math.Round(total / count, 2);

    /// <summary>Ageing bucket of a balance at a date: 0 = not due, 1 = 1–30 days late, 2 = 31–60, 3 = 61–90, 4 = over 90.</summary>
    public static int Bucket(DateOnly dueDate, DateOnly asOf)
    {
        var late = asOf.DayNumber - dueDate.DayNumber;
        return late <= 0 ? 0 : late <= 30 ? 1 : late <= 60 ? 2 : late <= 90 ? 3 : 4;
    }

    public static KpiAgeingDto Ageing(IEnumerable<(DateOnly DueDate, decimal Balance)> open, DateOnly asOf, decimal? dso)
    {
        var buckets = new decimal[5];
        var overdue = 0;
        foreach (var (due, balance) in open)
        {
            var b = Bucket(due, asOf);
            buckets[b] += balance;
            if (b > 0)
            {
                overdue++;
            }
        }

        return new KpiAgeingDto(buckets.Sum(), buckets[0], buckets[1], buckets[2], buckets[3], buckets[4], overdue, dso);
    }
}
