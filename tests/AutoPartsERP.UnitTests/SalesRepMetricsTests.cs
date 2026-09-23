using AutoPartsERP.Application.Features.SalesReps;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class SalesRepMetricsTests
{
    [Theory]
    [InlineData("2026-01-01", "2026-01-31", 1)]
    [InlineData("2026-01-15", "2026-03-02", 3)]
    [InlineData("2025-11-01", "2026-02-28", 4)]
    [InlineData("2026-05-10", "2026-05-10", 1)]
    [InlineData("2026-05-10", "2026-04-10", 0)]
    public void MonthsSpanned_CountsCalendarMonthsTouched(string from, string to, int expected)
    {
        SalesRepMetrics.MonthsSpanned(DateOnly.Parse(from), DateOnly.Parse(to)).Should().Be(expected);
    }

    [Fact]
    public void Target_IsMonthlyTargetTimesMonths()
    {
        SalesRepMetrics.Target(1000m, new DateOnly(2026, 1, 20), new DateOnly(2026, 3, 1)).Should().Be(3000m);
    }

    [Theory]
    [InlineData(1000, 2.5, 25)]
    [InlineData(333.33, 3, 10)]      // rounded to cents
    [InlineData(0, 10, 0)]
    [InlineData(-50, 10, 0)]         // more returns than sales earns nothing, not a negative commission
    public void Commission_IsPercentOfNetBase(double baseUsd, double pct, double expected)
    {
        SalesRepMetrics.Commission((decimal)baseUsd, (decimal)pct).Should().Be((decimal)expected);
    }

    [Fact]
    public void DefaultPeriod_IsMonthToDate()
    {
        SalesRepMetrics.DefaultPeriod(new DateOnly(2026, 9, 23)).Should().Be((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 23)));
    }
}
