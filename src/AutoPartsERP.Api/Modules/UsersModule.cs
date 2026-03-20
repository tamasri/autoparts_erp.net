namespace AutoPartsERP.Api.Modules;

public sealed class UsersModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users").RequireAuthorization();

        group.MapPost("/", async Task<IResult> (CreateUserRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var idempotencyKey = EndpointRequestHelpers.GetIdempotencyKey(httpContext);
                var result = await sender.Send(new CreateUserCommand(request, idempotencyKey), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPut("/{userId:guid}", async Task<IResult> (Guid userId, UpdateUserRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new UpdateUserCommand(userId, request), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/{userId:guid}/deactivate", async Task<IResult> (Guid userId, DeactivateUserPayload payload, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new DeactivateUserCommand(userId, payload.Reason, payload.ReasonCode), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{userId:guid}/roles", async Task<IResult> (Guid userId, AssignUserRolesRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new AssignRolesToUserCommand(userId, request, null), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapGet("/", async Task<IResult> (
                int page,
                int pageSize,
                bool? isActive,
                string? roleCode,
                string? search,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var pageNumber = page <= 0 ? 1 : page;
                var size = pageSize <= 0 ? 20 : pageSize;
                var result = await sender.Send(new GetUsersQuery(pageNumber, size, isActive, roleCode, search), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/{userId:guid}", async Task<IResult> (Guid userId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetUserByIdQuery(userId), cancellationToken);
                return result.ToApiResult();
            });
    }

    public sealed record DeactivateUserPayload(string Reason, string? ReasonCode);
}
