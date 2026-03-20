namespace AutoPartsERP.Api.Modules;

public sealed class PaymentsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/payments").RequireAuthorization();

        group.MapGet("/", async Task<IResult> (
                int page,
                int pageSize,
                Guid? customerId,
                string? paymentMethod,
                DateOnly? fromDate,
                DateOnly? toDate,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetPaymentsQuery(
                    page <= 0 ? 1 : page,
                    pageSize <= 0 ? 20 : pageSize,
                    customerId,
                    paymentMethod,
                    fromDate,
                    toDate), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/", async Task<IResult> (CreatePaymentRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CreatePaymentCommand(
                    request.PaymentType,
                    request.CustomerId,
                    request.PaymentDate,
                    request.PaymentMethod,
                    request.AmountSyp,
                    request.AmountUsd,
                    request.FxRateId,
                    request.ReferenceNumber,
                    request.BankName,
                    request.ChequeNumber,
                    request.ChequeDate,
                    request.Notes,
                    EndpointRequestHelpers.GetIdempotencyKey(httpContext)),
                    cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/allocate", async Task<IResult> (Guid id, AllocatePaymentRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new AllocatePaymentCommand(
                    id,
                    request.Allocations,
                    request.Notes,
                    EndpointRequestHelpers.GetIdempotencyKey(httpContext)),
                    cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/reverse", async Task<IResult> (Guid id, ReversePaymentRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ReversePaymentCommand(
                    id,
                    request.Reason,
                    EndpointRequestHelpers.GetIdempotencyKey(httpContext)),
                    cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();
    }
}
