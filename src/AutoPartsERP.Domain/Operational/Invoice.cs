using AutoPartsERP.Domain.Extensions;
using AutoPartsERP.Domain.Operational.Enums;

namespace AutoPartsERP.Domain.Operational;

public sealed class Invoice : AuditableEntity
{
    private readonly List<InvoiceLine> _lines = new();

    private Invoice(
        Guid id,
        Guid customerId,
        DateOnly invoiceDate,
        DateOnly dueDate,
        Guid fxRateId,
        decimal fxRateSnapshot,
        InvoiceType invoiceType,
        Guid createdBy,
        Guid? salesRepId)
        : base(id)
    {
        CustomerId = customerId;
        InvoiceDate = invoiceDate;
        DueDate = dueDate;
        FxRateId = fxRateId;
        FxRateSnapshot = fxRateSnapshot;
        Type = invoiceType;
        Status = InvoiceStatus.Draft;
        SalesRepId = salesRepId;
        CreatedBy = createdBy;
    }

    public string? InvoiceNumber { get; private set; }

    public InvoiceType Type { get; private set; }

    public InvoiceStatus Status { get; private set; }

    public Guid CustomerId { get; private set; }

    public DateOnly InvoiceDate { get; private set; }

    public DateOnly DueDate { get; private set; }

    public string? DeliveryAddress { get; private set; }

    public decimal SubtotalSyp { get; private set; }

    public decimal SubtotalUsd { get; private set; }

    public decimal DiscountAmountSyp { get; private set; }

    public decimal DiscountAmountUsd { get; private set; }

    public decimal DeliveryFeeSyp { get; private set; }

    public decimal DeliveryFeeUsd { get; private set; }

    public decimal TaxAmountSyp { get; private set; }

    public decimal TaxAmountUsd { get; private set; }

    public decimal TotalSyp { get; private set; }

    public decimal TotalUsd { get; private set; }

    public decimal PaidSyp { get; private set; }

    public decimal PaidUsd { get; private set; }

    public decimal BalanceSyp => TotalSyp - PaidSyp;

    public decimal BalanceUsd => TotalUsd - PaidUsd;

    public Guid FxRateId { get; private set; }

    public decimal FxRateSnapshot { get; private set; }

    public Guid? SalesRepId { get; private set; }

    public DateTimeOffset? PostedAt { get; private set; }

    public Guid? PostedBy { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    public Guid? VoidedBy { get; private set; }

    public string? VoidReason { get; private set; }

    public Guid CreatedBy { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public IReadOnlyCollection<InvoiceLine> Lines => _lines.AsReadOnly();

    public static Result<Invoice> Create(
        Guid customerId,
        DateOnly invoiceDate,
        DateOnly dueDate,
        Guid fxRateId,
        decimal fxRateSnapshot,
        InvoiceType invoiceType,
        Guid createdBy,
        Guid? salesRepId = null,
        string? deliveryAddress = null)
    {
        if (customerId == Guid.Empty)
        {
            return Result<Invoice>.Failure(new Error("Invoice.CustomerRequired", "Customer is required."));
        }

        if (fxRateId == Guid.Empty)
        {
            return Result<Invoice>.Failure(new Error("Invoice.FxRateRequired", "FX rate is required."));
        }

        if (dueDate < invoiceDate)
        {
            return Result<Invoice>.Failure(new Error("Invoice.InvalidDueDate", "Due date cannot be before invoice date."));
        }

        var invoice = new Invoice(
            Guid.NewGuid(),
            customerId,
            invoiceDate,
            dueDate,
            fxRateId,
            fxRateSnapshot,
            invoiceType,
            createdBy,
            salesRepId)
        {
            DeliveryAddress = deliveryAddress?.Trim()
        };

        return Result<Invoice>.Success(invoice);
    }

    public Result<InvoiceLine> AddLine(
        Guid skuId,
        Guid? batchId,
        Guid locationId,
        decimal qty,
        decimal priceSyp,
        decimal priceUsd,
        decimal discountPct,
        decimal costSyp,
        decimal costUsd,
        decimal fxRate,
        bool isPriceOverride,
        string? overrideReason = null)
    {
        if (Status != InvoiceStatus.Draft)
        {
            return Result<InvoiceLine>.Failure(
                new Error("Invoice.InvalidState", $"Cannot modify a {Status.HumanizeAr()} invoice"));
        }

        if (qty <= 0)
        {
            return Result<InvoiceLine>.Failure(new Error("Invoice.InvalidQuantity", "Line quantity must be greater than zero."));
        }

        if (priceSyp < 0 || priceUsd < 0)
        {
            return Result<InvoiceLine>.Failure(new Error("Invoice.InvalidPrice", "Line prices must be non-negative."));
        }

        if (isPriceOverride && string.IsNullOrWhiteSpace(overrideReason))
        {
            return Result<InvoiceLine>.Failure(new Error("Invoice.OverrideReasonRequired", "Price override reason is required."));
        }

        var line = new InvoiceLine(
            Guid.NewGuid(),
            Id,
            _lines.Count + 1,
            skuId,
            batchId,
            locationId,
            qty,
            priceSyp,
            priceUsd,
            discountPct,
            costSyp,
            costUsd,
            fxRate,
            isPriceOverride,
            overrideReason?.Trim());

        _lines.Add(line);
        RecalculateTotals();
        Touch();
        return Result<InvoiceLine>.Success(line);
    }

    public Result Confirm(Guid by)
    {
        if (Status != InvoiceStatus.Draft)
        {
            return Result.Failure(new Error("Invoice.InvalidState", $"Cannot confirm a {Status.HumanizeAr()} invoice."));
        }

        Status = InvoiceStatus.Confirmed;
        UpdatedBy = by;
        Touch();
        return Result.Success();
    }

    public Result Post(Guid by)
    {
        if (Status != InvoiceStatus.Confirmed)
        {
            return Result.Failure(new Error("Invoice.InvalidState", "Only confirmed invoices can be posted."));
        }

        if (_lines.Count == 0)
        {
            return Result.Failure(new Error("Invoice.NoLines", "Cannot post an invoice with no lines."));
        }

        Status = InvoiceStatus.Posted;
        PostedAt = DateTimeOffset.UtcNow;
        PostedBy = by;
        UpdatedBy = by;
        Touch();
        return Result.Success();
    }

    public Result Void(string reason, Guid by)
    {
        if (Status != InvoiceStatus.Posted)
        {
            return Result.Failure(new Error("Invoice.InvalidState", "Only posted invoices can be voided."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(new Error("Invoice.VoidReasonRequired", "Void reason is required."));
        }

        if (PaidSyp != 0m || PaidUsd != 0m)
        {
            return Result.Failure(new Error("Invoice.HasAllocations", "Cannot void an invoice that has payment allocations."));
        }

        Status = InvoiceStatus.Void;
        VoidReason = reason.Trim();
        VoidedAt = DateTimeOffset.UtcNow;
        VoidedBy = by;
        UpdatedBy = by;
        Touch();
        return Result.Success();
    }

    public void SetDeliveryFee(decimal syp, decimal usd)
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException($"Cannot modify a {Status.HumanizeAr()} invoice");
        }

        if (syp < 0 || usd < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(syp), "Delivery fee cannot be negative.");
        }

        DeliveryFeeSyp = syp;
        DeliveryFeeUsd = usd;
        RecalculateTotals();
        Touch();
    }

    public void RecordPayment(decimal syp, decimal usd)
    {
        if (syp < 0 || usd < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(syp), "Payment values cannot be negative.");
        }

        PaidSyp += syp;
        PaidUsd += usd;
        Touch();
    }

    private void RecalculateTotals()
    {
        SubtotalSyp = _lines.Sum(line => line.LineTotalSyp);
        SubtotalUsd = _lines.Sum(line => line.LineTotalUsd);
        TotalSyp = SubtotalSyp - DiscountAmountSyp + DeliveryFeeSyp + TaxAmountSyp;
        TotalUsd = SubtotalUsd - DiscountAmountUsd + DeliveryFeeUsd + TaxAmountUsd;
    }
}
