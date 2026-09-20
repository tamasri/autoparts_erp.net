using AutoPartsERP.Application.Features.Locations;
using AutoPartsERP.Application.Features.Wms;

namespace AutoPartsERP.Api.Modules;

public sealed record CreateLocationRequest(string Code, string Name, string Type, Guid? ParentId);

public sealed record UpdateLocationRequest(string Name, string Type, Guid? ParentId, bool IsActive);

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

        group.MapGet("/overview", async Task<IResult> (bool? includeInactive, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetLocationOverviewQuery(includeInactive == true), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/", async Task<IResult> (CreateLocationRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CreateLocationCommand(request.Code, request.Name, request.Type, request.ParentId), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPut("/{id:guid}", async Task<IResult> (Guid id, UpdateLocationRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new UpdateLocationCommand(id, request.Name, request.Type, request.ParentId, request.IsActive), cancellationToken);
                return result.ToApiResult();
            });
    }
}
