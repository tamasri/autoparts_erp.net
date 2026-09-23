using AutoPartsERP.Application.Features.Dashboard;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class KpiMathTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 30);

    [Theory]
    [InlineData(0, 0)]       // due today: not late
    [InlineData(-5, 0)]      // due later
    [InlineData(1, 1)]
    [InlineData(30, 1)]
    [InlineData(31, 2)]
    [InlineData(60, 2)]
    [InlineData(61, 3)]
    [InlineData(90, 3)]
    [InlineData(91, 4)]
    public void Bucket_By_Days_Late(int daysLate, int bucket)
    {
        KpiMath.Bucket(AsOf.AddDays(-daysLate), AsOf).Should().Be(bucket);
    }

    [Fact]
    public void Ageing_Splits_And_Counts_Overdue()
    {
        var a = KpiMath.Ageing([(AsOf.AddDays(5), 100m), (AsOf.AddDays(-10), 50m), (AsOf.AddDays(-45), 20m), (AsOf.AddDays(-75), 5m), (AsOf.AddDays(-200), 1m)], AsOf, 12.5m);

        a.TotalUsd.Should().Be(176);
        (a.NotDueUsd, a.Days1To30Usd, a.Days31To60Usd, a.Days61To90Usd, a.Over90Usd).Should().Be((100m, 50m, 20m, 5m, 1m));
        a.OverdueCount.Should().Be(4);
        a.DaysSalesOutstanding.Should().Be(12.5m);
    }

    [Fact]
    public void Dso_Is_Receivables_In_Days_Of_Average_Sales()
    {
        // 30-day period, 3000 sold → 100 a day; 450 owed → 4.5 days
        KpiMath.Dso(450, 3000, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)).Should().Be(4.5m);
        KpiMath.Dso(450, 0, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)).Should().BeNull();
    }

    [Fact]
    public void Margin_And_Average()
    {
        KpiMath.MarginPct(200, 50).Should().Be(25m);
        KpiMath.MarginPct(0, 10).Should().BeNull();
        KpiMath.MarginPct(-5, 10).Should().BeNull();
        KpiMath.Average(100, 3).Should().Be(33.33m);
        KpiMath.Average(100, 0).Should().BeNull();
    }
}
