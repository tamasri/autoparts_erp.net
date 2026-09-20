using AutoPartsERP.Application.Features.Purchasing;

namespace AutoPartsERP.Api.Modules;

/// <summary>Supplier bills (purchase invoices) and supplier payments.</summary>
public sealed class PurchasingModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var bills = app.MapGroup("/api/v1/purchase-invoices").RequireAuthorization();

        bills.MapGet("/", async Task<IResult> (int? page, int? pageSize, string? status, Guid? supplierPartyId, string? search, bool? openOnly, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetPurchaseInvoicesQuery(page ?? 1, pageSize ?? 20, status, supplierPartyId, search, openOnly == true), ct)).ToApiResult());

        bills.MapGet("/{id:guid}", async Task<IResult> (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetPurchaseInvoiceByIdQuery(id), ct)).ToApiResult());

        bills.MapPost("/", async Task<IResult> (CreatePurchaseInvoiceRequest request, HttpContext http, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CreatePurchaseInvoiceCommand(request, EndpointRequestHelpers.GetIdempotencyKey(http)), ct)).ToApiResult())
            .WithIdempotency();

        bills.MapPost("/{id:guid}/post", async Task<IResult> (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new PostPurchaseInvoiceCommand(id), ct)).ToApiResult());

        bills.MapPost("/{id:guid}/void", async Task<IResult> (Guid id, VoidPurchaseInvoiceRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new VoidPurchaseInvoiceCommand(id, request.Reason), ct)).ToApiResult());

        var payments = app.MapGroup("/api/v1/supplier-payments").RequireAuthorization();

        payments.MapGet("/", async Task<IResult> (int? page, int? pageSize, Guid? supplierPartyId, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetSupplierPaymentsQuery(page ?? 1, pageSize ?? 20, supplierPartyId), ct)).ToApiResult());

        payments.MapPost("/", async Task<IResult> (CreateSupplierPaymentRequest request, HttpContext http, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CreateSupplierPaymentCommand(request, EndpointRequestHelpers.GetIdempotencyKey(http)), ct)).ToApiResult())
            .WithIdempotency();

        payments.MapPost("/{id:guid}/reverse", async Task<IResult> (Guid id, ReverseSupplierPaymentRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new ReverseSupplierPaymentCommand(id, request.Reason), ct)).ToApiResult());
    }
}
