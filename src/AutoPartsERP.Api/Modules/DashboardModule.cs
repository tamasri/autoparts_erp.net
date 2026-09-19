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
    }
}
