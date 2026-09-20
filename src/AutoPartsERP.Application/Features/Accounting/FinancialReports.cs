using AutoPartsERP.Contracts.Accounting;

namespace AutoPartsERP.Application.Features.Accounting;

/// <summary>The chart of accounts as a tree: parents before children, each account with its depth.</summary>
public sealed class ChartTree
{
    public sealed record Node(ErpNextAccount Account, int Depth, IReadOnlyList<Node> Children);

    private readonly Dictionary<string, Node> _byName;

    private ChartTree(IReadOnlyList<Node> roots, IReadOnlyList<Node> ordered)
    {
        Roots = roots;
        Ordered = ordered;
        _byName = ordered.ToDictionary(n => n.Account.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<Node> Roots { get; }

    /// <summary>Depth-first order, the way the chart is read top to bottom.</summary>
    public IReadOnlyList<Node> Ordered { get; }

    public Node? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>Assets and expenses grow with debits; liabilities, equity and income grow with credits.</summary>
    public static bool IsDebitNormal(string? rootType) => rootType is "Asset" or "Expense";

    public static ChartTree Build(IEnumerable<ErpNextAccount> accounts)
    {
        var list = accounts.ToList();
        var known = list.Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
        var children = list.Where(a => a.ParentAccount is not null && known.Contains(a.ParentAccount))
            .GroupBy(a => a.ParentAccount!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // A parent must come before its children in Ordered, so its slot is reserved first and filled once the children exist.
        var ordered = new List<Node>(list.Count);
        var roots = new List<Node>();
        void Visit(ErpNextAccount account, int depth, List<Node> into)
        {
            var slot = ordered.Count;
            ordered.Add(null!);
            var kids = new List<Node>();
            foreach (var child in children.GetValueOrDefault(account.Name, []))
            {
                Visit(child, depth + 1, kids);
            }

            var node = new Node(account, depth, kids);
            ordered[slot] = node;
            into.Add(node);
        }

        foreach (var root in list.Where(a => a.ParentAccount is null || !known.Contains(a.ParentAccount)))
        {
            Visit(root, 0, roots);
        }

        return new ChartTree(roots, ordered);
    }
}

/// <summary>
/// The financial statements, built from the ledger totals ERPNext returns per account. Nothing here talks to ERPNext, so the arithmetic
/// (roll-up of groups, net profit, balance checks) is tested on its own. Debits are positive in every signed figure.
/// </summary>
public static class FinancialReports
{
    private const decimal Tolerance = 0.01m;

    private readonly record struct Movement(decimal Opening, decimal Debit, decimal Credit)
    {
        public decimal Closing => Opening + Debit - Credit;

        public static Movement operator +(Movement a, Movement b) => new(a.Opening + b.Opening, a.Debit + b.Debit, a.Credit + b.Credit);
    }

    public static TrialBalanceDto TrialBalance(
        ChartTree chart, IReadOnlyList<ErpNextGlBalance> before, IReadOnlyList<ErpNextGlBalance> period, DateOnly from, DateOnly to, bool includeZero)
    {
        var lines = new List<TrialBalanceLineDto>();
        var movements = Roll(chart, before, period, out var orphans);

        foreach (var node in chart.Ordered)
        {
            var m = movements[node.Account.Name];
            if (!includeZero && m.Opening == 0 && m.Debit == 0 && m.Credit == 0)
            {
                continue;
            }

            lines.Add(new TrialBalanceLineDto(node.Account.Name, node.Account.AccountName, node.Account.ParentAccount, node.Depth, node.Account.IsGroup,
                node.Account.RootType, m.Opening, m.Debit, m.Credit, m.Closing));
        }

        // Accounts the ledger knows but the chart no longer lists (disabled ones) still count, or the totals would not balance.
        foreach (var (account, m) in orphans)
        {
            lines.Add(new TrialBalanceLineDto(account, account, null, 0, false, null, m.Opening, m.Debit, m.Credit, m.Closing));
        }

        var ledgers = lines.Where(l => !l.IsGroup).ToList();
        var totalDebit = ledgers.Sum(l => Math.Max(l.Closing, 0));
        var totalCredit = ledgers.Sum(l => Math.Max(-l.Closing, 0));
        return new TrialBalanceDto(from, to, lines, totalDebit, totalCredit, Math.Abs(totalDebit - totalCredit) < Tolerance);
    }

    public static BalanceSheetDto BalanceSheet(ChartTree chart, IReadOnlyList<ErpNextGlBalance> cumulative, DateOnly asOf)
    {
        var movements = Roll(chart, [], cumulative, out _);

        List<StatementLineDto> Section(string rootType) => chart.Ordered
            .Where(n => n.Account.RootType == rootType && movements[n.Account.Name].Closing != 0)
            .Select(n => Statement(n, movements[n.Account.Name].Closing, ChartTree.IsDebitNormal(rootType)))
            .ToList();

        var assets = Section("Asset");
        var liabilities = Section("Liability");
        var equity = Section("Equity");

        // Profit that has not been closed into equity yet: everything earned less everything spent, to date.
        var netProfit = -chart.Roots.Where(n => n.Account.RootType is "Income" or "Expense").Sum(n => movements[n.Account.Name].Closing);
        var totalAssets = Total(chart, movements, "Asset");
        var totalLiabilities = Total(chart, movements, "Liability");
        var totalEquity = Total(chart, movements, "Equity") + netProfit;
        return new BalanceSheetDto(asOf, assets, liabilities, equity, netProfit, totalAssets, totalLiabilities, totalEquity,
            Math.Abs(totalAssets - (totalLiabilities + totalEquity)) < Tolerance);
    }

    public static ProfitLossDto ProfitLoss(ChartTree chart, IReadOnlyList<ErpNextGlBalance> period, DateOnly from, DateOnly to)
    {
        var movements = Roll(chart, [], period, out _);

        List<StatementLineDto> Section(string rootType) => chart.Ordered
            .Where(n => n.Account.RootType == rootType && (movements[n.Account.Name].Debit != 0 || movements[n.Account.Name].Credit != 0))
            .Select(n => Statement(n, movements[n.Account.Name].Closing, ChartTree.IsDebitNormal(rootType)))
            .ToList();

        var income = Section("Income");
        var expenses = Section("Expense");
        var totalIncome = Total(chart, movements, "Income");
        var totalExpenses = Total(chart, movements, "Expense");
        return new ProfitLossDto(from, to, income, expenses, totalIncome, totalExpenses, totalIncome - totalExpenses);
    }

    /// <summary>Every account's balance to date in its natural direction (groups include what is under them).</summary>
    public static Dictionary<string, decimal> NaturalBalances(ChartTree chart, IReadOnlyList<ErpNextGlBalance> cumulative)
    {
        var movements = Roll(chart, [], cumulative, out _);
        return chart.Ordered.ToDictionary(n => n.Account.Name, n => Natural(movements[n.Account.Name].Closing, n.Account.RootType), StringComparer.Ordinal);
    }

    /// <summary>An account's balance in its natural direction, for a total or a statement line.</summary>
    public static decimal Natural(decimal signed, string? rootType) => ChartTree.IsDebitNormal(rootType) ? signed : -signed;

    private static StatementLineDto Statement(ChartTree.Node node, decimal signedClosing, bool debitNormal) =>
        new(node.Account.Name, node.Account.AccountName, node.Account.ParentAccount, node.Depth, node.Account.IsGroup, debitNormal ? signedClosing : -signedClosing);

    private static decimal Total(ChartTree chart, Dictionary<string, Movement> movements, string rootType) =>
        chart.Roots.Where(n => n.Account.RootType == rootType).Sum(n => Natural(movements[n.Account.Name].Closing, rootType));

    /// <summary>Totals for every account: ledgers straight from the ledger figures, groups as the sum of what is under them.</summary>
    private static Dictionary<string, Movement> Roll(
        ChartTree chart, IReadOnlyList<ErpNextGlBalance> before, IReadOnlyList<ErpNextGlBalance> period, out Dictionary<string, Movement> orphans)
    {
        var opening = before.GroupBy(b => b.Account, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(b => b.Debit - b.Credit), StringComparer.Ordinal);
        var moves = period.GroupBy(b => b.Account, StringComparer.Ordinal).ToDictionary(g => g.Key, g => (Debit: g.Sum(b => b.Debit), Credit: g.Sum(b => b.Credit)), StringComparer.Ordinal);

        var result = new Dictionary<string, Movement>(StringComparer.Ordinal);
        Movement Compute(ChartTree.Node node)
        {
            var own = new Movement(opening.GetValueOrDefault(node.Account.Name), moves.GetValueOrDefault(node.Account.Name).Debit, moves.GetValueOrDefault(node.Account.Name).Credit);
            var total = node.Children.Aggregate(own, (sum, child) => sum + Compute(child));
            result[node.Account.Name] = total;
            return total;
        }

        foreach (var root in chart.Roots)
        {
            Compute(root);
        }

        orphans = new Dictionary<string, Movement>(StringComparer.Ordinal);
        foreach (var name in opening.Keys.Concat(moves.Keys).Distinct(StringComparer.Ordinal).Where(n => !result.ContainsKey(n)))
        {
            orphans[name] = new Movement(opening.GetValueOrDefault(name), moves.GetValueOrDefault(name).Debit, moves.GetValueOrDefault(name).Credit);
        }

        return result;
    }
}
