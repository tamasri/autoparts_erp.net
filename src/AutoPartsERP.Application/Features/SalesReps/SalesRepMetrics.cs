namespace AutoPartsERP.Application.Features.SalesReps;

/// <summary>The arithmetic behind a rep's figures, kept out of SQL so it can be tested.</summary>
public static class SalesRepMetrics
{
    /// <summary>Calendar months the period touches (1 Jan – 15 Mar = 3). A monthly target is multiplied by this.</summary>
    public static int MonthsSpanned(DateOnly from, DateOnly to) =>
        to < from ? 0 : ((to.Year - from.Year) * 12) + to.Month - from.Month + 1;

    /// <summary>Commission on the net sales base (returns already subtracted); never negative.</summary>
    public static decimal Commission(decimal commissionBaseUsd, decimal commissionPct) =>
        commissionBaseUsd <= 0 ? 0 : Math.Round(commissionBaseUsd * commissionPct / 100m, 2);

    public static decimal Target(decimal monthlyTargetUsd, DateOnly from, DateOnly to) => monthlyTargetUsd * MonthsSpanned(from, to);

    /// <summary>The period shown when none is given: the current month up to today.</summary>
    public static (DateOnly From, DateOnly To) DefaultPeriod(DateOnly today) => (new DateOnly(today.Year, today.Month, 1), today);
}
