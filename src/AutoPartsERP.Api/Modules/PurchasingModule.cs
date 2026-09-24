using AutoPartsERP.Application.Features.Purchasing;
using AutoPartsERP.Application.Features.Purchasing.LandedCost;

namespace AutoPartsERP.Api.Modules;

/// <summary>Supplier bills (purchase invoices), purchase returns, landed cost vouchers and supplier payments.</summary>
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

        // Purchase returns: the bill's lines with what can still be returned, and a draft return of some of them.
        bills.MapGet("/{id:guid}/returnable", async Task<IResult> (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetPurchaseReturnableQuery(id), ct)).ToApiResult());

        bills.MapPost("/{id:guid}/returns", async Task<IResult> (Guid id, CreateReturnRequest request, HttpContext http, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CreatePurchaseReturnCommand(id, request, EndpointRequestHelpers.GetIdempotencyKey(http)), ct)).ToApiResult())
            .WithIdempotency();

        // Landed cost vouchers (قيد رسملة مصاريف الشراء): drafts are saved and previewed, then posted (with approval) or voided.
        var landed = app.MapGroup("/api/v1/landed-costs").RequireAuthorization();

        landed.MapGet("/", async Task<IResult> (int? page, int? pageSize, string? status, Guid? purchaseInvoiceId, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetLandedCostsQuery(page ?? 1, pageSize ?? 20, status, purchaseInvoiceId), ct)).ToApiResult());

        landed.MapGet("/{id:guid}", async Task<IResult> (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetLandedCostQuery(id), ct)).ToApiResult());

        landed.MapPost("/", async Task<IResult> (SaveLandedCostRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new SaveLandedCostCommand(null, request), ct)).ToApiResult());

        landed.MapPut("/{id:guid}", async Task<IResult> (Guid id, SaveLandedCostRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new SaveLandedCostCommand(id, request), ct)).ToApiResult());

        landed.MapPost("/{id:guid}/post", async Task<IResult> (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new PostLandedCostCommand(id), ct)).ToApiResult());

        landed.MapPost("/{id:guid}/void", async Task<IResult> (Guid id, VoidLandedCostRequest request, ISender sender, CancellationToken ct) =>
            (await sender.Send(new VoidLandedCostCommand(id, request.Reason), ct)).ToApiResult());

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
