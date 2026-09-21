namespace AutoPartsERP.Application.Features.Accounting;

/// <summary>
/// The arithmetic of a paged ledger statement, kept apart from the ERPNext calls so it can be tested on its own.
/// Balances are in the account's natural direction (<c>sign</c> = +1 for debit-normal accounts, -1 otherwise), like every other statement line.
/// </summary>
public static class LedgerPaging
{
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 5000;

    public static int ClampPageSize(int requested) => Math.Clamp(requested <= 0 ? DefaultPageSize : requested, 10, MaxPageSize);

    public static int Offset(int page, int size) => (Math.Max(page, 1) - 1) * size;

    /// <summary>The balance in force just before the first line of a page: the opening balance moved by every line of the pages before it.</summary>
    public static decimal PageStartBalance(decimal sign, decimal openingSigned, decimal offsetDebit, decimal offsetCredit) =>
        sign * (openingSigned + offsetDebit - offsetCredit);

    /// <summary>The balance at the end of the whole period, whatever page is shown.</summary>
    public static decimal ClosingBalance(decimal sign, decimal openingSigned, decimal periodDebit, decimal periodCredit) =>
        sign * (openingSigned + periodDebit - periodCredit);

    /// <summary>Running balance after each line, starting from <paramref name="startBalance"/>.</summary>
    public static IReadOnlyList<decimal> RunningBalances(decimal sign, decimal startBalance, IEnumerable<(decimal Debit, decimal Credit)> lines)
    {
        var balance = startBalance;
        var result = new List<decimal>();
        foreach (var (debit, credit) in lines)
        {
            balance += sign * (debit - credit);
            result.Add(balance);
        }

        return result;
    }

    public static int PageCount(long totalCount, int size) => (int)Math.Max(1, (totalCount + size - 1) / size);
}
