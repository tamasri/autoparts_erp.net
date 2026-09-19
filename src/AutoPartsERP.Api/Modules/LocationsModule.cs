using AutoPartsERP.Application.Features.Locations;

namespace AutoPartsERP.Api.Modules;

public sealed class LocationsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/locations").RequireAuthorization();

        group.MapGet("/", async Task<IResult> (string? type, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetLocationsQuery(type), cancellationToken);
                return result.ToApiResult();
            });
    }
}
