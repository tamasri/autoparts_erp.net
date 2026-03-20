namespace AutoPartsERP.Api.Modules;

public sealed class CustomersModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/customers").RequireAuthorization();

        group.MapGet("/", async Task<IResult> (
                int page,
                int pageSize,
                string? type,
                bool? isActive,
                string? searchTerm,
                Guid? assignedSalesRep,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetCustomersQuery(
                    new CustomerQueryRequest(
                        page <= 0 ? 1 : page,
                        pageSize <= 0 ? 20 : pageSize,
                        type,
                        isActive,
                        searchTerm,
                        assignedSalesRep)), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetCustomerByIdQuery(id), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/", async Task<IResult> (CreateCustomerRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CreateCustomerCommand(request, EndpointRequestHelpers.GetIdempotencyKey(httpContext)), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPut("/{id:guid}", async Task<IResult> (Guid id, UpdateCustomerRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new UpdateCustomerCommand(id, request), cancellationToken);
                return result.ToApiResult();
            });

        group.MapDelete("/{id:guid}", async Task<IResult> (Guid id, string? reason, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new DeactivateCustomerCommand(id, reason ?? "Deactivated"), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/{id:guid}/statement", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetCustomerAccountStatementQuery(id), cancellationToken);
                return result.ToApiResult();
            });
    }
}
