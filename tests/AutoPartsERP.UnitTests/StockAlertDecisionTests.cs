using AutoPartsERP.Application.Features.InventoryAlerts;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class StockAlertDecisionTests
{
    private static StockAlertScanner.Decision D(decimal available, decimal level, params string[] open) =>
        StockAlertScanner.Decide(available, level, open);

    [Fact]
    public void At_or_below_the_level_raises_low_stock()
    {
        D(5, 5).Should().BeEquivalentTo(new StockAlertScanner.Decision(StockAlertTypes.LowStock, "HIGH", []));
        D(2, 5).Raise.Should().Be(StockAlertTypes.LowStock);
    }

    [Fact]
    public void Nothing_left_is_out_of_stock_and_critical() =>
        D(0, 5).Should().BeEquivalentTo(new StockAlertScanner.Decision(StockAlertTypes.OutOfStock, "CRITICAL", []));

    [Fact]
    public void Above_the_level_raises_nothing_and_resolves_what_is_open() =>
        D(6, 5, StockAlertTypes.LowStock).Should().BeEquivalentTo(new StockAlertScanner.Decision(null, null, [StockAlertTypes.LowStock]));

    [Fact]
    public void An_open_alert_of_the_right_type_is_not_raised_again() =>
        D(3, 5, StockAlertTypes.LowStock).Should().BeEquivalentTo(new StockAlertScanner.Decision(null, null, []));

    [Fact]
    public void Running_out_replaces_low_stock_with_out_of_stock()
    {
        var d = D(0, 5, StockAlertTypes.LowStock);

        d.Raise.Should().Be(StockAlertTypes.OutOfStock);
        d.Resolve.Should().Equal(StockAlertTypes.LowStock);
    }

    [Fact]
    public void Restocking_below_the_level_turns_out_of_stock_back_into_low_stock()
    {
        var d = D(2, 5, StockAlertTypes.OutOfStock);

        d.Raise.Should().Be(StockAlertTypes.LowStock);
        d.Resolve.Should().Equal(StockAlertTypes.OutOfStock);
    }

    [Fact]
    public void Items_without_a_reorder_level_are_not_watched_and_old_alerts_close()
    {
        D(0, 0).Raise.Should().BeNull();
        D(0, 0, StockAlertTypes.OutOfStock).Resolve.Should().Equal(StockAlertTypes.OutOfStock);
    }
}
