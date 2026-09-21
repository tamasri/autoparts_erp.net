using AutoPartsERP.Application.Features.Accounting;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class LedgerPagingTests
{
    // 37 lines: every third is a credit, the rest debits of growing size.
    private static readonly (decimal Debit, decimal Credit)[] Lines =
        Enumerable.Range(1, 37).Select(i => i % 3 == 0 ? (0m, i * 1.5m) : (i * 2m, 0m)).ToArray();

    /// <summary>What the statement builds for one page: the balance before it comes from the totals of the lines skipped, not from the lines themselves.</summary>
    private static IReadOnlyList<decimal> Page(decimal sign, decimal opening, int page, int size)
    {
        var offset = LedgerPaging.Offset(page, size);
        var skipped = Lines.Take(offset).ToArray();
        var start = LedgerPaging.PageStartBalance(sign, opening, skipped.Sum(l => l.Debit), skipped.Sum(l => l.Credit));
        return LedgerPaging.RunningBalances(sign, start, Lines.Skip(offset).Take(size));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 250.75)]
    [InlineData(-1, 120)]
    [InlineData(-1, -40)]
    public void EveryPage_ContinuesTheBalanceOfThePageBefore(decimal sign, decimal opening)
    {
        var wholeStatement = LedgerPaging.RunningBalances(sign, sign * opening, Lines);

        var stitched = Enumerable.Range(1, LedgerPaging.PageCount(Lines.Length, 10)).SelectMany(p => Page(sign, opening, p, 10)).ToList();

        stitched.Should().Equal(wholeStatement);
    }

    [Fact]
    public void ThirdPage_DoesNotDependOnTheFirstPageSize()
    {
        // The earlier implementation re-read only one page of history, so page 3 onwards started from the wrong balance.
        var wholeStatement = LedgerPaging.RunningBalances(1, 0, Lines);

        Page(1, 0, 3, 10).Should().Equal(wholeStatement.Skip(20).Take(10));
        Page(1, 0, 4, 10).Should().Equal(wholeStatement.Skip(30));
    }

    [Fact]
    public void ClosingBalance_IsTheBalanceAfterTheLastLine()
    {
        var closing = LedgerPaging.ClosingBalance(1, 100, Lines.Sum(l => l.Debit), Lines.Sum(l => l.Credit));

        closing.Should().Be(LedgerPaging.RunningBalances(1, 100, Lines).Last());
    }

    [Fact]
    public void ACreditNormalAccount_GrowsWithCredits()
    {
        LedgerPaging.RunningBalances(-1, 0, [(0m, 50m), (20m, 0m)]).Should().Equal(50m, 30m);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-5, 100)]
    [InlineData(3, 10)]
    [InlineData(100, 100)]
    [InlineData(999999, LedgerPaging.MaxPageSize)]
    public void PageSize_IsKeptInsideTheAllowedRange(int requested, int expected) =>
        LedgerPaging.ClampPageSize(requested).Should().Be(expected);

    [Theory]
    [InlineData(0, 100, 1)]
    [InlineData(100, 100, 1)]
    [InlineData(101, 100, 2)]
    [InlineData(1000, 100, 10)]
    public void PageCount_RoundsUp_AndIsNeverZero(long total, int size, int expected) =>
        LedgerPaging.PageCount(total, size).Should().Be(expected);
}
