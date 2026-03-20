namespace AutoPartsERP.Api.Modules;

public sealed class ReportsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reports").RequireAuthorization();

        group.MapGet("/profit-loss", async Task<IResult> (int year, int? month, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetProfitLossQuery(year, month), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/inventory-value", async Task<IResult> (Guid? locationId, Guid? categoryId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetInventoryValueQuery(locationId, categoryId), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/inventory-value/export", async Task<IResult> (Guid? locationId, Guid? categoryId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ExportInventoryValueCommand(locationId, categoryId), cancellationToken);
                return result.IsSuccess && result.Value is not null
                    ? Results.File(result.Value, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "inventory-value.xlsx")
                    : result.ToApiResult();
            });

        group.MapGet("/batch-trace/{batchId:guid}", async Task<IResult> (Guid batchId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetBatchTraceQuery(batchId), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/account-statements", async Task<IResult> (Guid customerId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetCustomerAccountStatementQuery(customerId), cancellationToken);
                return result.ToApiResult();
            });
    }
}
