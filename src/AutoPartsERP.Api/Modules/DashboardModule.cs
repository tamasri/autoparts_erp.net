using AutoPartsERP.Application.Features.Dashboard;

namespace AutoPartsERP.Api.Modules;

public sealed class DashboardModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dashboard").RequireAuthorization();

        group.MapGet("/summary", async Task<IResult> (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetDashboardSummaryQuery(), cancellationToken);
                return result.ToApiResult();
            });

        // Business KPIs for a period (defaults: the current month to today) with optional warehouse / customer / rep / category filters.
        group.MapGet("/kpis", async Task<IResult> (DateOnly? from, DateOnly? to, Guid? warehouseId, Guid? customerId, Guid? salesRepId, Guid? categoryId, ISender sender, CancellationToken cancellationToken) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var query = new GetBusinessKpisQuery(from ?? new DateOnly(today.Year, today.Month, 1), to ?? today, warehouseId, customerId, salesRepId, categoryId);
            return (await sender.Send(query, cancellationToken)).ToApiResult();
        });
    }
}
