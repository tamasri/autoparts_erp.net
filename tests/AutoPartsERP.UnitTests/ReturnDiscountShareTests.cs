using AutoPartsERP.Application.Common.Pricing;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class ReturnDiscountShareTests
{
    [Fact]
    public void A_partial_return_gives_back_the_discount_in_proportion_to_the_returned_lines()
    {
        // $100 of lines with $10 off; $40 of lines come back → $4 of the discount goes back.
        DocumentDiscount.ReturnShare(10m, 100m, 40m, 0m, returnsEverythingLeft: false).Should().Be(4m);
    }

    [Fact]
    public void The_last_return_gives_back_exactly_what_is_left_so_the_returns_add_up_to_the_whole_discount()
    {
        // $10 off $30 of lines, returned in three thirds: rounding would give 3.3333 × 3 = 9.9999; the last one closes the gap.
        var first = DocumentDiscount.ReturnShare(10m, 30m, 10m, 0m, false);
        var second = DocumentDiscount.ReturnShare(10m, 30m, 10m, first, false);
        var last = DocumentDiscount.ReturnShare(10m, 30m, 10m, first + second, returnsEverythingLeft: true);

        (first + second + last).Should().Be(10m);
    }

    [Fact]
    public void A_return_never_gives_back_more_discount_than_is_left()
    {
        DocumentDiscount.ReturnShare(10m, 100m, 100m, 9m, returnsEverythingLeft: false).Should().Be(1m);
        DocumentDiscount.ReturnShare(10m, 100m, 50m, 12m, returnsEverythingLeft: true).Should().Be(0m);
    }

    [Fact]
    public void No_discount_or_no_lines_give_nothing_back()
    {
        DocumentDiscount.ReturnShare(0m, 100m, 40m, 0m, false).Should().Be(0m);
        DocumentDiscount.ReturnShare(10m, 0m, 40m, 0m, false).Should().Be(0m);
    }
}
