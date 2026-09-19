using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Domain.Constants;
using AutoPartsERP.Infrastructure.Jobs;

namespace AutoPartsERP.Api.Modules;

/// <summary>
/// In-app control surface for the ERPNext hand-off, so operators never need the Hangfire
/// dashboard (which requires a bearer header a browser navigation cannot send) or the ERPNext UI.
/// </summary>
public sealed class ErpNextSyncModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/erpnext")
            .RequireAuthorization(policy => policy.RequireRole(RoleCodes.SystemAdministrator));

        group.MapPost("/sync", (IBackgroundJobClient jobs, IErpNextClient client) =>
        {
            if (!client.IsEnabled)
            {
                return Results.Conflict(new { error = "ERPNext integration is disabled (Erpnext:Enabled=false)." });
            }

            var jobId = jobs.Enqueue<SyncCatalogToErpNextJob>(job => job.RunAsync(CancellationToken.None));
            return Results.Accepted(value: new { jobId });
        });

        group.MapGet("/sync-log", async Task<IResult> (IDbConnectionFactory connectionFactory, CancellationToken cancellationToken) =>
        {
            await using var connection = await connectionFactory.CreateAsync(cancellationToken);
            var rows = await connection.QueryAsync(new CommandDefinition(
                """
                SELECT local_entity_type AS "localEntityType", erpnext_doctype AS "erpnextDoctype",
                       status, erpnext_name AS "erpnextName", last_error AS "lastError",
                       attempt_count AS "attemptCount", updated_at AS "updatedAt"
                FROM erpnext_sync_log
                ORDER BY updated_at DESC
                LIMIT 200;
                """,
                cancellationToken: cancellationToken));
            return Results.Ok(rows);
        });
    }
}
