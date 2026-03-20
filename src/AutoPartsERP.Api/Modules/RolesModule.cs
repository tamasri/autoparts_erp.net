namespace AutoPartsERP.Api.Modules;

public sealed class RolesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/roles").RequireAuthorization();

        group.MapPost("/", async Task<IResult> (CreateRoleRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var idempotencyKey = EndpointRequestHelpers.GetIdempotencyKey(httpContext);
                var result = await sender.Send(new CreateRoleCommand(request, idempotencyKey), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{roleId:guid}/permissions/grant", async Task<IResult> (Guid roleId, PermissionPayload request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GrantPermissionToRoleCommand(roleId, request.PermissionCode), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{roleId:guid}/permissions/revoke", async Task<IResult> (Guid roleId, PermissionPayload request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new RevokePermissionFromRoleCommand(roleId, request.PermissionCode), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapGet("/", async Task<IResult> (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetRolesQuery(), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/{roleId:guid}", async Task<IResult> (Guid roleId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetRoleByIdQuery(roleId), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/permissions/all", async Task<IResult> (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetAllPermissionsQuery(), cancellationToken);
                return result.ToApiResult();
            });
    }

    public sealed record PermissionPayload(string PermissionCode);
}
