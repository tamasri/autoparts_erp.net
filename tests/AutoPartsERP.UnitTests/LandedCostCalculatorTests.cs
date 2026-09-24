using AutoPartsERP.Application.Features.Purchasing.LandedCost;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class LandedCostCalculatorTests
{
    private static readonly Guid SkuA = Guid.NewGuid();
    private static readonly Guid SkuB = Guid.NewGuid();
    private static readonly Guid LineA = Guid.NewGuid();
    private static readonly Guid LineB = Guid.NewGuid();

    // 10 units of A at $6 (value 60) and 30 units of B at $1 (value 30).
    private static readonly IReadOnlyList<LandedLine> Lines = [new(LineA, SkuA, 10, 6), new(LineB, SkuB, 30, 1)];
    private static readonly Dictionary<Guid, decimal> PlentyOnHand = new() { [SkuA] = 100, [SkuB] = 100 };

    private static LandedCostResult Run(IReadOnlyList<LandedCharge> charges, IReadOnlyDictionary<Guid, decimal>? onHand = null) =>
        LandedCostCalculator.Calculate(Lines, charges, onHand ?? PlentyOnHand).Value!;

    [Fact]
    public void By_value_follows_quantity_times_cost()
    {
        var r = Run([new(Guid.NewGuid(), 90m, LandedCostCalculator.ByValue)]);
        r.Shares.Single(s => s.LineId == LineA).Amount.Should().Be(60m);
        r.Shares.Single(s => s.LineId == LineB).Amount.Should().Be(30m);
    }

    [Fact]
    public void By_quantity_and_equally()
    {
        var byQty = Run([new(Guid.NewGuid(), 40m, LandedCostCalculator.ByQuantity)]);
        byQty.Shares.Single(s => s.LineId == LineA).Amount.Should().Be(10m);
        byQty.Shares.Single(s => s.LineId == LineB).Amount.Should().Be(30m);

        var equal = Run([new(Guid.NewGuid(), 40m, LandedCostCalculator.Equally)]);
        equal.Shares.Should().OnlyContain(s => s.Amount == 20m);
    }

    [Fact]
    public void The_shares_of_a_charge_add_up_to_it_exactly()
    {
        var r = Run([new(Guid.NewGuid(), 10m, LandedCostCalculator.Equally), new(Guid.NewGuid(), 100m / 3m, LandedCostCalculator.ByValue)]);
        r.Shares.GroupBy(s => s.ChargeId).Select(g => g.Sum(s => s.Amount)).Should().Equal(10m, 100m / 3m);
        r.Effects.Sum(e => e.Allocated).Should().Be(10m + 100m / 3m);
    }

    [Fact]
    public void Everything_is_capitalized_while_the_goods_are_all_on_hand()
    {
        var r = Run([new(Guid.NewGuid(), 90m, LandedCostCalculator.ByValue)]);
        r.Effects.Should().OnlyContain(e => e.Capitalized == e.Allocated && e.Expensed == 0);
    }

    [Fact]
    public void The_share_of_goods_already_sold_goes_to_cost_of_goods_sold()
    {
        // A: 10 bought, 4 left → 40 % of its $60 stays in stock ($24), $36 is expensed. B: none left → all $30 expensed.
        var r = Run([new(Guid.NewGuid(), 90m, LandedCostCalculator.ByValue)], new Dictionary<Guid, decimal> { [SkuA] = 4, [SkuB] = 0 });
        var a = r.Effects.Single(e => e.SkuId == SkuA);
        (a.Capitalized, a.Expensed).Should().Be((24m, 36m));
        var b = r.Effects.Single(e => e.SkuId == SkuB);
        (b.Capitalized, b.Expensed).Should().Be((0m, 30m));
    }

    [Fact]
    public void Returned_goods_carry_nothing_and_a_fully_returned_bill_is_refused()
    {
        var r = LandedCostCalculator.Calculate([new(LineA, SkuA, 10, 6), new(LineB, SkuB, 0, 1)], [new(Guid.NewGuid(), 50m, LandedCostCalculator.ByQuantity)], PlentyOnHand);
        r.Value!.Shares.Should().ContainSingle(s => s.LineId == LineA && s.Amount == 50m);

        LandedCostCalculator.Calculate([new(LineA, SkuA, 0, 6)], [new(Guid.NewGuid(), 5m, LandedCostCalculator.ByValue)], PlentyOnHand)
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public void A_value_split_over_goods_without_value_is_refused()
    {
        LandedCostCalculator.Calculate([new(LineA, SkuA, 5, 0)], [new(Guid.NewGuid(), 5m, LandedCostCalculator.ByValue)], PlentyOnHand)
            .Error.Code.Should().Be("LandedCost.NoBasis");
    }
}
