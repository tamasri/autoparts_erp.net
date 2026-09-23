using AutoPartsERP.Domain.Constants;
using AutoPartsERP.Application.Features.Accounting;
using AutoPartsERP.Application.Features.Accounting.Consistency;
using AutoPartsERP.Infrastructure.Imports;

namespace AutoPartsERP.Api.Modules;

/// <summary>
/// The accounting screens: chart of accounts, entry types and manual entries, tags, reconciliation and the financial reports.
/// The ledger itself is ERPNext's; every request is authorised by the accounting permissions, not by role names.
/// </summary>
public sealed class AccountingModule : ICarterModule
{
    private const long MaxFileBytes = 2 * 1024 * 1024;

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/accounting").RequireAuthorization();

        MapChart(group);
        MapEntries(group);
        MapTags(group);
        MapReports(group);
        MapReconciliation(group);

        group.MapGet("/erpnext/consistency", async Task<IResult> (ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetErpNextConsistencyQuery(), ct)).ToApiResult());
        group.MapGet("/erpnext/reference/{kind}", async Task<IResult> (string kind, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetErpNextReferenceQuery(kind), ct)).ToApiResult());
    }

    private static void MapChart(RouteGroupBuilder group)
    {
        group.MapGet("/accounts", async Task<IResult> (bool? balances, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetChartOfAccountsQuery(balances == true), ct)).ToApiResult());

        group.MapGet("/accounts/mapping", async Task<IResult> (ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetAccountMappingQuery(), ct)).ToApiResult());

        group.MapGet("/accounts/types", () => Results.Ok(ApiResponse.Success(ErpNextAccountTypes.All.Order(StringComparer.Ordinal).ToArray())));

        group.MapPost("/accounts", async Task<IResult> (CreateAccountRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new CreateAccountCommand(request), ct)).ToApiResult());

        // The account name has spaces and dashes ("Cash - AB"), so it travels in the query string, not the path.
        group.MapPut("/accounts", async Task<IResult> (string name, UpdateAccountRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new UpdateAccountCommand(name, request), ct)).ToApiResult());

        group.MapGet("/accounts/import/template", (string? format) =>
            string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
                ? Results.File(AccountFileReader.BuildTemplateCsv(), "text/csv; charset=utf-8", "accounts-template.csv")
                : Results.File(AccountFileReader.BuildTemplateXlsx(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "accounts-template.xlsx"));

        group.MapPost("/accounts/import", async Task<IResult> (IFormFile file, bool? dryRun, ISender sender, CancellationToken ct) =>
            {
                if (file is null || file.Length == 0)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Import.NoFile", detail: "Choose a .xlsx or .csv file.");
                }

                if (file.Length > MaxFileBytes)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Import.TooLarge", detail: "The file is larger than 2 MB.");
                }

                if (Path.GetExtension(file.FileName).ToLowerInvariant() is not (".xlsx" or ".csv"))
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Import.Format", detail: "Only .xlsx and .csv files are supported.");
                }

                IReadOnlyList<ImportAccountRow> rows;
                try
                {
                    await using var buffer = new MemoryStream();
                    await file.CopyToAsync(buffer, ct);
                    buffer.Position = 0;
                    rows = AccountFileReader.Read(buffer, file.FileName);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Import.Unreadable", detail: ex is InvalidDataException ? ex.Message : "The file could not be read.");
                }

                return (await sender.Send(new ImportAccountsCommand(rows, dryRun ?? true), ct)).ToApiResult();
            })
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimiting.Heavy);
    }

    private static void MapEntries(RouteGroupBuilder group)
    {
        group.MapGet("/entry-types", async Task<IResult> (bool? includeInactive, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetEntryTypesQuery(includeInactive == true), ct)).ToApiResult());

        group.MapPost("/entry-types", async Task<IResult> (SaveEntryTypeRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new SaveEntryTypeCommand(null, request), ct)).ToApiResult());

        group.MapPut("/entry-types/{id:guid}", async Task<IResult> (Guid id, SaveEntryTypeRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new SaveEntryTypeCommand(id, request), ct)).ToApiResult());

        var entries = group.MapGroup("/entries");

        entries.MapGet("/", async Task<IResult> (int? page, int? pageSize, string? status, Guid? entryTypeId, string? search, DateOnly? from, DateOnly? to, Guid? tagId, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetJournalEntriesQuery(page ?? 1, pageSize ?? 20, status, entryTypeId, search, from, to, tagId), ct)).ToApiResult());

        entries.MapGet("/{id:guid}", async Task<IResult> (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetJournalEntryQuery(id), ct)).ToApiResult());

        entries.MapPost("/", async Task<IResult> (SaveJournalEntryRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new SaveJournalEntryCommand(null, request), ct)).ToApiResult());

        entries.MapPut("/{id:guid}", async Task<IResult> (Guid id, SaveJournalEntryRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new SaveJournalEntryCommand(id, request), ct)).ToApiResult());

        entries.MapPost("/{id:guid}/post", async Task<IResult> (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new PostJournalEntryCommand(id), ct)).ToApiResult());

        entries.MapPost("/{id:guid}/void", async Task<IResult> (Guid id, VoidJournalEntryRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new VoidJournalEntryCommand(id, request.Reason), ct)).ToApiResult());

        entries.MapDelete("/{id:guid}", async Task<IResult> (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new DeleteJournalEntryCommand(id), ct)).ToApiResult());
    }

    private static void MapTags(RouteGroupBuilder group)
    {
        var tags = group.MapGroup("/tags");

        tags.MapGet("/", async Task<IResult> (ISender sender, CancellationToken ct) => (await sender.Send(new GetTagsQuery(), ct)).ToApiResult());

        tags.MapPost("/", async Task<IResult> (SaveTagRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new SaveTagCommand(null, request), ct)).ToApiResult());

        tags.MapPut("/{id:guid}", async Task<IResult> (Guid id, SaveTagRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new SaveTagCommand(id, request), ct)).ToApiResult());

        tags.MapDelete("/{id:guid}", async Task<IResult> (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new DeleteTagCommand(id), ct)).ToApiResult());

        tags.MapPost("/{id:guid}/attach", async Task<IResult> (Guid id, TagTargetRequest target, ISender sender, CancellationToken ct) =>
            (await sender.Send(new SetTagCommand(id, target, true), ct)).ToApiResult());

        tags.MapPost("/{id:guid}/detach", async Task<IResult> (Guid id, TagTargetRequest target, ISender sender, CancellationToken ct) =>
            (await sender.Send(new SetTagCommand(id, target, false), ct)).ToApiResult());
    }

    private static void MapReports(RouteGroupBuilder group)
    {
        var reports = group.MapGroup("/reports");

        reports.MapGet("/trial-balance", async Task<IResult> (DateOnly from, DateOnly to, bool? includeZero, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetTrialBalanceQuery(from, to, includeZero == true), ct)).ToApiResult());

        reports.MapGet("/balance-sheet", async Task<IResult> (DateOnly asOf, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetBalanceSheetQuery(asOf), ct)).ToApiResult());

        reports.MapGet("/profit-loss", async Task<IResult> (DateOnly from, DateOnly to, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetProfitLossStatementQuery(from, to), ct)).ToApiResult());

        reports.MapGet("/ledger", async Task<IResult> (string account, DateOnly from, DateOnly to, string? party, Guid? tagId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetLedgerStatementQuery(account, from, to, party, tagId, page ?? 1, pageSize ?? LedgerPaging.DefaultPageSize), ct)).ToApiResult());

        reports.MapGet("/party-balances", async Task<IResult> (string partyType, DateOnly asOf, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetPartyBalancesQuery(partyType.ToUpperInvariant(), asOf), ct)).ToApiResult());
    }

    private static void MapReconciliation(RouteGroupBuilder group)
    {
        var rec = group.MapGroup("/reconciliation");

        rec.MapGet("/", async Task<IResult> (string? account, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetReconciliationsQuery(account), ct)).ToApiResult());

        rec.MapGet("/{id:guid}", async Task<IResult> (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetReconciliationQuery(id), ct)).ToApiResult());

        rec.MapGet("/candidates", async Task<IResult> (string account, DateOnly asOf, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetReconcileCandidatesQuery(account, asOf), ct)).ToApiResult());

        rec.MapGet("/statement", async Task<IResult> (string account, DateOnly asOf, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetReconciliationStatementQuery(account, asOf), ct)).ToApiResult());

        rec.MapPost("/", async Task<IResult> (CompleteReconciliationRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new CompleteReconciliationCommand(request), ct)).ToApiResult());

        rec.MapDelete("/{id:guid}", async Task<IResult> (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new UndoReconciliationCommand(id), ct)).ToApiResult());
    }
}
