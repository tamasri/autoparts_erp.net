namespace AutoPartsERP.Application.Features.SalesReps;

/// <summary>The arithmetic behind a rep's figures, kept out of SQL so it can be tested.</summary>
public static class SalesRepMetrics
{
    /// <summary>Calendar months the period touches (1 Jan – 15 Mar = 3). A monthly target is multiplied by this.</summary>
    public static int MonthsSpanned(DateOnly from, DateOnly to) =>
        to < from ? 0 : ((to.Year - from.Year) * 12) + to.Month - from.Month + 1;

    /// <summary>
    /// Commission on the gross profit of the rep's invoices (revenue after line and invoice discounts, without delivery or tax, minus the
    /// cost of the goods; returns already subtracted); never negative.
    /// </summary>
    public static decimal Commission(decimal grossProfitUsd, decimal commissionPct) =>
        grossProfitUsd <= 0 ? 0 : Math.Round(grossProfitUsd * commissionPct / 100m, 2, MidpointRounding.AwayFromZero);

    public static decimal Target(decimal monthlyTargetUsd, DateOnly from, DateOnly to) => monthlyTargetUsd * MonthsSpanned(from, to);

    /// <summary>How much of the target was reached, in percent (one decimal); null when there is no target.</summary>
    public static decimal? Achievement(decimal achievedUsd, decimal targetUsd) =>
        targetUsd <= 0 ? null : Math.Round(achievedUsd / targetUsd * 100m, 1);

    /// <summary>Gross profit as a share of the revenue it was earned on, in percent (one decimal); null without revenue.</summary>
    public static decimal? Margin(decimal grossProfitUsd, decimal revenueUsd) =>
        revenueUsd <= 0 ? null : Math.Round(grossProfitUsd / revenueUsd * 100m, 1);

    /// <summary>A part of a whole in percent (one decimal); null when the whole is nothing.</summary>
    public static decimal? Share(decimal part, decimal whole) => whole <= 0 ? null : Math.Round(part / whole * 100m, 1);

    /// <summary>The period shown when none is given: the current month up to today.</summary>
    public static (DateOnly From, DateOnly To) DefaultPeriod(DateOnly today) => (new DateOnly(today.Year, today.Month, 1), today);
}
