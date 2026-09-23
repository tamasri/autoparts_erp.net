namespace AutoPartsERP.Application.Features.SalesReps;

/// <summary>A new invoice or a customer can only be given to an active rep (the column has a foreign key; this turns it into a clear message).</summary>
public static class SalesRepGuard
{
    public static readonly Error NotActive = new("SalesRep.NotActive", "The sales representative does not exist or is inactive.");

    public static Task<bool> IsActiveAsync(DbConnection connection, DbTransaction? transaction, Guid userId, CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM sales_reps WHERE user_id = @userId AND is_active);", new { userId }, transaction, cancellationToken: cancellationToken));
}
