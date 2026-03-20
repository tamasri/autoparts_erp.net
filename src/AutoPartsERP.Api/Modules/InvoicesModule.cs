namespace AutoPartsERP.Api.Modules;

public sealed class InvoicesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/invoices").RequireAuthorization();

        group.MapGet("/", async Task<IResult> (
                int page,
                int pageSize,
                string? status,
                string? type,
                Guid? customerId,
                DateOnly? fromDate,
                DateOnly? toDate,
                string? searchTerm,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetInvoicesQuery(
                    page <= 0 ? 1 : page,
                    pageSize <= 0 ? 20 : pageSize,
                    status,
                    type,
                    customerId,
                    fromDate,
                    toDate,
                    searchTerm), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetInvoiceByIdQuery(id), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/{id:guid}/pdf", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetInvoicePdfQuery(id), cancellationToken);
                return result.IsSuccess && result.Value is not null
                    ? Results.File(result.Value, "application/pdf", $"invoice-{id}.pdf")
                    : result.ToApiResult();
            });

        group.MapPost("/", async Task<IResult> (CreateInvoiceRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CreateInvoiceCommand(
                    request.CustomerId,
                    request.InvoiceDate,
                    request.DueDate,
                    request.FxRateId,
                    request.SalesRepId,
                    request.InvoiceType,
                    request.DeliveryFeeSyp,
                    request.DeliveryFeeUsd,
                    request.Lines,
                    EndpointRequestHelpers.GetIdempotencyKey(httpContext)), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/lines", async Task<IResult> (Guid id, CreateInvoiceLineRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new AddInvoiceLineCommand(id, request, EndpointRequestHelpers.GetIdempotencyKey(httpContext)), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPut("/{id:guid}/delivery-fee", async Task<IResult> (Guid id, UpdateDeliveryFeeRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new UpdateDeliveryFeeCommand(
                    id,
                    request.DeliveryFeeSyp,
                    request.DeliveryFeeUsd,
                    EndpointRequestHelpers.GetIdempotencyKey(httpContext)), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/confirm", async Task<IResult> (Guid id, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ConfirmInvoiceCommand(id, EndpointRequestHelpers.GetIdempotencyKey(httpContext)), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/{id:guid}/post", async Task<IResult> (Guid id, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new PostInvoiceCommand(
                    id,
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    EndpointRequestHelpers.GetIdempotencyKey(httpContext)),
                    cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/void", async Task<IResult> (Guid id, VoidInvoiceRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new VoidInvoiceCommand(
                    id,
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    request.Reason,
                    EndpointRequestHelpers.GetIdempotencyKey(httpContext)),
                    cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();
    }
}
