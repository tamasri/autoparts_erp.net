using AutoPartsERP.Application.Common.Pricing;

namespace AutoPartsERP.Application.Features.Invoices;

/// <summary>Invoice totals are worked out in one place: the database function <c>recalc_invoice_totals</c> (migration 21).</summary>
internal static class InvoiceTotals
{
    public static Task RecalculateAsync(DbConnection connection, DbTransaction? transaction, Guid invoiceId, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition("SELECT recalc_invoice_totals(@invoiceId);", new { invoiceId }, transaction, cancellationToken: cancellationToken));

    /// <summary>
    /// Sets the invoice discount (a percentage of the lines, or a dollar amount converted to lira at the invoice's own rate) and recalculates.
    /// Neither value clears it.
    /// </summary>
    public static async Task<Result> ApplyDiscountAsync(
        DbConnection connection, DbTransaction? transaction, Guid invoiceId, decimal? percent, decimal? amountUsd, CancellationToken cancellationToken)
    {
        var basis = await connection.QuerySingleAsync<(decimal SubtotalUsd, decimal Rate)>(new CommandDefinition(
            """
            SELECT COALESCE((SELECT SUM(line_total_usd) FROM invoice_lines WHERE invoice_id = @invoiceId), 0) AS SubtotalUsd, fx_rate_snapshot AS Rate
            FROM invoices WHERE id = @invoiceId;
            """,
            new { invoiceId }, transaction, cancellationToken: cancellationToken));

        var resolved = DocumentDiscount.Resolve(basis.SubtotalUsd, percent, amountUsd);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE invoices SET discount_pct = @Percent, discount_amount_usd = @Amount, discount_amount_syp = round(@Amount * @Rate, 4) WHERE id = @invoiceId;",
            new { invoiceId, resolved.Value!.Percent, resolved.Value.Amount, basis.Rate }, transaction, cancellationToken: cancellationToken));
        await RecalculateAsync(connection, transaction, invoiceId, cancellationToken);
        return Result.Success();
    }
}
