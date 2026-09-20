using AutoPartsERP.Domain.Constants;

namespace AutoPartsERP.Api.Modules;

/// <summary>
/// Read-only window onto ERPNext for accountants, inside our own UI: the chart of accounts, which account each application
/// event posts to, and the documents ERPNext holds (each linked back to the local record it came from when known).
/// </summary>
public sealed class ErpNextBrowseModule : ICarterModule
{
    // Legacy role code still present in seeded databases next to the RoleCodes vocabulary.
    private const string AccountantRole = "ACCOUNTANT";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/erpnext")
            .RequireAuthorization(policy => policy.RequireRole(RoleCodes.SystemAdministrator, RoleCodes.Auditor, RoleCodes.ComplianceOfficer, AccountantRole));

        group.MapGet("/status", (IErpNextClient client) => Results.Ok(new { enabled = client.IsEnabled }));

        group.MapGet("/accounts", async Task<IResult> (bool? balances, IErpNextClient client, CancellationToken cancellationToken) =>
        {
            var result = await client.GetChartOfAccountsAsync(balances == true, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new { items = result.Value })
                : Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: result.Error.Code, detail: result.Error.Message);
        });

        group.MapGet("/accounts/mapping", async Task<IResult> (IErpNextClient client, CancellationToken cancellationToken) =>
        {
            var result = await client.GetAccountMappingAsync(cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new { items = result.Value })
                : Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: result.Error.Code, detail: result.Error.Message);
        });

        group.MapGet("/documents/{doctype}", async Task<IResult> (
            string doctype,
            int? page,
            int? pageSize,
            string? search,
            IErpNextClient client,
            IDbConnectionFactory connectionFactory,
            CancellationToken cancellationToken) =>
        {
            var pageNumber = page is > 0 ? page.Value : 1;
            var size = Math.Clamp(pageSize is > 0 ? pageSize.Value : 20, 1, 100);
            var result = await client.ListDocumentsAsync(doctype, pageNumber, size, search, cancellationToken);
            if (result.IsFailure)
            {
                return Results.Problem(
                    statusCode: result.Error.Code == "ErpNext.DoctypeNotAllowed" ? StatusCodes.Status400BadRequest : StatusCodes.Status502BadGateway,
                    title: result.Error.Code,
                    detail: result.Error.Message);
            }

            var names = result.Value!.Rows
                .Select(r => r.TryGetValue("name", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() : null)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToArray();

            // Which of these does the application know about? (ERPNext name -> local entity from the sync log.)
            await using var connection = await connectionFactory.CreateAsync(cancellationToken);
            var links = (await connection.QueryAsync<(string Name, string EntityType, Guid EntityId)>(new CommandDefinition(
                """
                SELECT erpnext_name AS Name, local_entity_type AS EntityType, local_entity_id AS EntityId
                FROM erpnext_sync_log
                WHERE erpnext_doctype = @Doctype AND erpnext_name = ANY(@Names) AND status IN ('SYNCED', 'CANCELLED');
                """,
                new { Doctype = doctype, Names = names },
                cancellationToken: cancellationToken))).ToDictionary(x => x.Name, x => x);

            var items = result.Value.Rows.Select(row =>
            {
                var name = row.TryGetValue("name", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() : null;
                var dict = row.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
                if (name is not null && links.TryGetValue(name, out var link))
                {
                    dict["localEntityType"] = link.EntityType;
                    dict["localEntityId"] = link.EntityId;
                }

                return dict;
            }).ToList();

            return Results.Ok(new { columns = result.Value.Columns, items, pageNumber, pageSize = size, totalCount = result.Value.TotalCount });
        });
    }
}
