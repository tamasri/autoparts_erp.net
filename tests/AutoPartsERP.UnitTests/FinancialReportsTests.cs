using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Application.Features.Accounting;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class FinancialReportsTests
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly Dec31 = new(2026, 12, 31);

    // Assets > Cash, Debtors | Liabilities > Creditors | Equity > Capital | Income > Sales | Expenses > Rent
    private static ChartTree Chart() => ChartTree.Build(
    [
        new("Assets", "Assets", null, true, "Asset", null, "USD"),
        new("Cash", "Cash", "Assets", false, "Asset", "Cash", "USD"),
        new("Debtors", "Debtors", "Assets", false, "Asset", "Receivable", "USD"),
        new("Liabilities", "Liabilities", null, true, "Liability", null, "USD"),
        new("Creditors", "Creditors", "Liabilities", false, "Liability", "Payable", "USD"),
        new("Equity", "Equity", null, true, "Equity", null, "USD"),
        new("Capital", "Capital", "Equity", false, "Equity", null, "USD"),
        new("Income", "Income", null, true, "Income", null, "USD"),
        new("Sales", "Sales", "Income", false, "Income", "Income Account", "USD"),
        new("Expenses", "Expenses", null, true, "Expense", null, "USD"),
        new("Rent", "Rent", "Expenses", false, "Expense", "Expense Account", "USD")
    ]);

    // Owner puts in 1000, sells for 300 cash, pays 100 rent.
    private static readonly ErpNextGlBalance[] AllTime =
    [
        new("Cash", 1300, 100), new("Capital", 0, 1000), new("Sales", 0, 300), new("Rent", 100, 0)
    ];

    [Fact]
    public void Tree_ListsParentsBeforeChildren_WithDepth()
    {
        var chart = Chart();

        chart.Ordered.Select(n => n.Account.Name).Should().ContainInOrder("Assets", "Cash", "Debtors", "Liabilities", "Creditors");
        chart.Find("Cash")!.Depth.Should().Be(1);
        chart.Find("Assets")!.Depth.Should().Be(0);
    }

    [Fact]
    public void TrialBalance_BalancesAndRollsUpGroups()
    {
        var tb = FinancialReports.TrialBalance(Chart(), [], AllTime, Jan1, Dec31, includeZero: false);

        tb.IsBalanced.Should().BeTrue();
        tb.TotalDebit.Should().Be(1300);
        tb.TotalCredit.Should().Be(1300);
        var assets = tb.Lines.Single(l => l.Account == "Assets");
        assets.IsGroup.Should().BeTrue();
        assets.Closing.Should().Be(1200);
        tb.Lines.Should().NotContain(l => l.Account == "Debtors", "an account with no activity is hidden unless asked for");
    }

    [Fact]
    public void TrialBalance_CarriesTheOpeningBalanceForward()
    {
        ErpNextGlBalance[] before = [new("Cash", 1000, 0), new("Capital", 0, 1000)];
        ErpNextGlBalance[] period = [new("Cash", 300, 100), new("Sales", 0, 300), new("Rent", 100, 0)];

        var tb = FinancialReports.TrialBalance(Chart(), before, period, Jan1, Dec31, includeZero: false);

        var cash = tb.Lines.Single(l => l.Account == "Cash");
        cash.Opening.Should().Be(1000);
        cash.Debit.Should().Be(300);
        cash.Credit.Should().Be(100);
        cash.Closing.Should().Be(1200);
        tb.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void TrialBalance_KeepsLedgerAccountsMissingFromTheChart_SoTotalsStillBalance()
    {
        ErpNextGlBalance[] withDisabled = [.. AllTime, new("Old Bank", 50, 0), new("Capital", 0, 50)];

        var tb = FinancialReports.TrialBalance(Chart(), [], withDisabled, Jan1, Dec31, includeZero: false);

        tb.Lines.Should().Contain(l => l.Account == "Old Bank" && l.Closing == 50);
        tb.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void BalanceSheet_IncludesProfitToDateInEquity_AndBalances()
    {
        var sheet = FinancialReports.BalanceSheet(Chart(), AllTime, Dec31);

        sheet.TotalAssets.Should().Be(1200);
        sheet.TotalLiabilities.Should().Be(0);
        sheet.NetProfit.Should().Be(200);
        sheet.TotalEquity.Should().Be(1200);
        sheet.IsBalanced.Should().BeTrue();
        sheet.Assets.Single(l => l.Account == "Cash").Amount.Should().Be(1200);
        sheet.Equity.Single(l => l.Account == "Capital").Amount.Should().Be(1000, "a credit-normal account shows positive in its own direction");
    }

    [Fact]
    public void BalanceSheet_ShowsALiabilityAsPositive()
    {
        ErpNextGlBalance[] gl = [new("Cash", 500, 0), new("Creditors", 0, 500)];

        var sheet = FinancialReports.BalanceSheet(Chart(), gl, Dec31);

        sheet.Liabilities.Single(l => l.Account == "Creditors").Amount.Should().Be(500);
        sheet.TotalLiabilities.Should().Be(500);
        sheet.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void ProfitLoss_IsIncomeMinusExpenses()
    {
        var pl = FinancialReports.ProfitLoss(Chart(), AllTime, Jan1, Dec31);

        pl.TotalIncome.Should().Be(300);
        pl.TotalExpenses.Should().Be(100);
        pl.NetProfit.Should().Be(200);
        pl.Income.Should().Contain(l => l.Account == "Sales" && l.Amount == 300);
        pl.Expenses.Should().Contain(l => l.Account == "Rent" && l.Amount == 100);
    }

    [Fact]
    public void ProfitLoss_ReportsALoss_AsNegative()
    {
        ErpNextGlBalance[] gl = [new("Rent", 400, 0), new("Sales", 0, 100)];

        FinancialReports.ProfitLoss(Chart(), gl, Jan1, Dec31).NetProfit.Should().Be(-300);
    }

    [Fact]
    public void NaturalBalances_UseEachAccountsOwnDirection()
    {
        var balances = FinancialReports.NaturalBalances(Chart(), AllTime);

        balances["Cash"].Should().Be(1200);
        balances["Capital"].Should().Be(1000);
        balances["Assets"].Should().Be(1200);
        balances["Income"].Should().Be(300);
        balances["Rent"].Should().Be(100);
    }
}
