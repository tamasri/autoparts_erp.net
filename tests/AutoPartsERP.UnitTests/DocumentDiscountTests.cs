using AutoPartsERP.Application.Common.Pricing;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class DocumentDiscountTests
{
    [Fact]
    public void Percent_IsTakenOfTheSubtotal()
    {
        var r = DocumentDiscount.Resolve(250m, 10m, null);

        r.IsSuccess.Should().BeTrue();
        r.Value!.Percent.Should().Be(10m);
        r.Value.Amount.Should().Be(25m);
    }

    [Fact]
    public void Amount_IsKeptAsGiven()
    {
        var r = DocumentDiscount.Resolve(250m, null, 30m);

        r.IsSuccess.Should().BeTrue();
        r.Value!.Percent.Should().BeNull();
        r.Value.Amount.Should().Be(30m);
    }

    [Fact]
    public void Neither_MeansNoDiscount()
    {
        var r = DocumentDiscount.Resolve(250m, null, null);

        r.Value!.Amount.Should().Be(0m);
        r.Value.Percent.Should().BeNull();
    }

    [Fact]
    public void ZeroPercent_ClearsIt()
    {
        DocumentDiscount.Resolve(250m, 0m, null).Value!.Percent.Should().BeNull();
    }

    [Theory]
    [InlineData(10d, 5d)]      // both given
    [InlineData(101d, null)]   // over 100 %
    [InlineData(-1d, null)]    // negative %
    [InlineData(null, -5d)]    // negative amount
    [InlineData(null, 300d)]   // larger than the lines
    public void Invalid_IsRefused(double? percent, double? amount)
    {
        var r = DocumentDiscount.Resolve(250m, (decimal?)percent, (decimal?)amount);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("Validation.Discount");
    }

    [Fact]
    public void Percent_IsRoundedToFourDecimals()
    {
        DocumentDiscount.Resolve(10m, 33.33m, null).Value!.Amount.Should().Be(3.333m);
        DocumentDiscount.Resolve(0.12345m, 50m, null).Value!.Amount.Should().Be(0.0617m);
    }

    [Theory]
    [InlineData(200, 50, 0.75)]
    [InlineData(0, 0, 1)]
    [InlineData(100, 0, 1)]
    public void NetFactor_IsWhatRemainsPerUnit(double subtotal, double discount, double expected)
    {
        DocumentDiscount.NetFactor((decimal)subtotal, (decimal)discount).Should().Be((decimal)expected);
    }
}
