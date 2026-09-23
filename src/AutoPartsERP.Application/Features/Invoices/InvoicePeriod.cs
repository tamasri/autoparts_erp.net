using System.Data;
using Dapper;

namespace AutoPartsERP.Application.Features.Invoices;

/// <summary>
/// Period lock for sales invoices, checked against the date stored on the invoice (not the day the action is taken):
/// posting and voiding land in ERPNext on the invoice date, so that is the month that must be open.
/// </summary>
internal static class InvoicePeriod
{
    public const string Module = "INVOICES";

    public static Error Locked(DateOnly date) => new("PeriodLock.Locked", $"Period {date:yyyy-MM} is locked for module {Module}.");

    /// <summary>Success when the invoice's month is open (or the invoice does not exist; the caller reports that).</summary>
    public static async Task<Result> EnsureOpenAsync(
        IPeriodLockService periodLock, IDbConnection connection, IDbTransaction? transaction, Guid invoiceId, CancellationToken cancellationToken)
    {
        var date = await connection.ExecuteScalarAsync<DateOnly?>(new CommandDefinition(
            "SELECT invoice_date FROM invoices WHERE id = @invoiceId;", new { invoiceId }, transaction, cancellationToken: cancellationToken));
        return date is { } d && await periodLock.IsLockedAsync(d.Year, d.Month, Module, cancellationToken)
            ? Result.Failure(Locked(d))
            : Result.Success();
    }
}
