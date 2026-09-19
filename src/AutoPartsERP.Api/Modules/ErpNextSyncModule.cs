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

        group.MapGet("/sync-log", async Task<IResult> (
            int page,
            int pageSize,
            string? status,
            IDbConnectionFactory connectionFactory,
            CancellationToken cancellationToken) =>
        {
            var pageNumber = page <= 0 ? 1 : page;
            var size = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);
            await using var connection = await connectionFactory.CreateAsync(cancellationToken);
            var rows = (await connection.QueryAsync(new CommandDefinition(
                """
                SELECT local_entity_type AS "localEntityType", erpnext_doctype AS "erpnextDoctype",
                       status, erpnext_name AS "erpnextName", last_error AS "lastError",
                       attempt_count AS "attemptCount", updated_at AS "updatedAt"
                FROM erpnext_sync_log
                WHERE (@status::text IS NULL OR status = @status)
                ORDER BY updated_at DESC
                OFFSET @offset LIMIT @size;
                """,
                new { status = string.IsNullOrWhiteSpace(status) ? null : status, offset = (pageNumber - 1) * size, size },
                cancellationToken: cancellationToken))).ToList();
            var total = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT count(*) FROM erpnext_sync_log WHERE (@status::text IS NULL OR status = @status);",
                new { status = string.IsNullOrWhiteSpace(status) ? null : status },
                cancellationToken: cancellationToken));
            return Results.Ok(new { items = rows, pageNumber, pageSize = size, totalCount = total });
        });

        group.MapGet("/sync-summary", async Task<IResult> (IDbConnectionFactory connectionFactory, CancellationToken cancellationToken) =>
        {
            await using var connection = await connectionFactory.CreateAsync(cancellationToken);
            var rows = await connection.QueryAsync(new CommandDefinition(
                """
                SELECT erpnext_doctype AS "doctype", status, count(*)::int AS "count"
                FROM erpnext_sync_log
                GROUP BY erpnext_doctype, status
                ORDER BY erpnext_doctype, status;
                """,
                cancellationToken: cancellationToken));
            var errors = await connection.QueryAsync(new CommandDefinition(
                """
                SELECT erpnext_doctype AS "doctype", last_error AS "lastError"
                FROM erpnext_sync_log
                WHERE status = 'FAILED'
                ORDER BY updated_at DESC
                LIMIT 10;
                """,
                cancellationToken: cancellationToken));
            return Results.Ok(new { summary = rows, recentErrors = errors });
        });
    }
}
