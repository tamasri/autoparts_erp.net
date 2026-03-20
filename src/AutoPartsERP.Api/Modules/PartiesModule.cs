namespace AutoPartsERP.Api.Modules;

public sealed class PartiesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/parties").RequireAuthorization();

        group.MapGet("/", async Task<IResult> (
                int page,
                int pageSize,
                string? typeCode,
                bool? isActive,
                string? searchTerm,
                bool? hasCombinedStatement,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetPartiesQuery(new PartyQueryRequest(
                    page <= 0 ? 1 : page,
                    pageSize <= 0 ? 20 : pageSize,
                    typeCode,
                    isActive,
                    searchTerm,
                    hasCombinedStatement)), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetPartyByIdQuery(id), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/", async Task<IResult> (CreatePartyRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CreatePartyCommand(request, EndpointRequestHelpers.GetIdempotencyKey(httpContext)), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPut("/{id:guid}", async Task<IResult> (Guid id, UpdatePartyRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new UpdatePartyCommand(id, request), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/{id:guid}/types", async Task<IResult> (
                Guid id,
                RequestPartyTypeAssignmentRequest request,
                HttpContext httpContext,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new RequestPartyTypeAssignmentCommand(
                    id,
                    request.TypeCode,
                    request.Reason,
                    EndpointRequestHelpers.GetIdempotencyKey(httpContext)), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapDelete("/{id:guid}/types/{typeCode}", async Task<IResult> (
                Guid id,
                string typeCode,
                string? reason,
                HttpContext httpContext,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new DeactivatePartyTypeAssignmentCommand(
                    id,
                    typeCode,
                    string.IsNullOrWhiteSpace(reason) ? "Deactivated by request." : reason.Trim(),
                    EndpointRequestHelpers.GetIdempotencyKey(httpContext)), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapGet("/{id:guid}/statement/combined", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetPartyCombinedStatementQuery(id), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/{id:guid}/statement/ar", async Task<IResult> (
                Guid id,
                IDbConnectionFactory dbConnectionFactory,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                await using var connection = await dbConnectionFactory.CreateOpenConnectionAsync(cancellationToken);
                var customerId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                    "SELECT id FROM customers WHERE party_id = @PartyId ORDER BY created_at LIMIT 1;",
                    new { PartyId = id },
                    cancellationToken: cancellationToken));

                if (!customerId.HasValue || customerId.Value == Guid.Empty)
                {
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Party.CustomerMissing", detail: "No customer profile exists for this party.");
                }

                var result = await sender.Send(new GetCustomerAccountStatementQuery(customerId.Value), cancellationToken);
                return result.ToApiResult();
            });
    }
}
