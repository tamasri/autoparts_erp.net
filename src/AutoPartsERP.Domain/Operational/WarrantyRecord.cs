using AutoPartsERP.Domain.Extensions;
using AutoPartsERP.Domain.Operational.Enums;

namespace AutoPartsERP.Domain.Operational;

public sealed class WarrantyRecord : AuditableEntity
{
    private WarrantyRecord(
        Guid id,
        Guid invoiceLineId,
        Guid skuId,
        Guid? batchId,
        Guid customerId,
        DateOnly saleDate,
        DateOnly expiryDate,
        Guid createdBy)
        : base(id)
    {
        WarrantyNumber = $"WR-{DateTime.UtcNow:yyyy}-{Random.Shared.Next(1, 99999):D5}";
        InvoiceLineId = invoiceLineId;
        SkuId = skuId;
        BatchId = batchId;
        CustomerId = customerId;
        SaleDate = saleDate;
        ExpiryDate = expiryDate;
        Status = WarrantyStatus.Active;
        CreatedBy = createdBy;
    }

    public string WarrantyNumber { get; private set; } = string.Empty;

    public Guid InvoiceId { get; private set; }

    public Guid InvoiceLineId { get; private set; }

    public Guid SkuId { get; private set; }

    public Guid? BatchId { get; private set; }

    public Guid CustomerId { get; private set; }

    public DateOnly SaleDate { get; private set; }

    public DateOnly ExpiryDate { get; private set; }

    public DateOnly? ClaimDate { get; private set; }

    public WarrantyStatus Status { get; private set; }

    public string? ClaimDescription { get; private set; }

    public string? Resolution { get; private set; }

    public Guid? ReplacementSkuId { get; private set; }

    public Guid? ReplacementBatchId { get; private set; }

    public Guid? ReplacementInvoiceId { get; private set; }

    public Guid? ProcessedBy { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public string? RejectionReason { get; private set; }

    public Guid CreatedBy { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static WarrantyRecord Create(
        Guid invoiceLineId,
        Guid skuId,
        Guid? batchId,
        Guid customerId,
        DateOnly saleDate,
        int warrantyMonths,
        Guid createdBy)
    {
        var expiryDate = saleDate.AddMonths(warrantyMonths);

        return new WarrantyRecord(
            Guid.NewGuid(),
            invoiceLineId,
            skuId,
            batchId,
            customerId,
            saleDate,
            expiryDate,
            createdBy);
    }

    public Result Claim(string description, DateOnly claimDate)
    {
        if (Status != WarrantyStatus.Active)
        {
            return Result.Failure(new Error("Warranty.InvalidState", "Only active warranty records can be claimed."));
        }

        if (claimDate > ExpiryDate)
        {
            var expiryAt = new DateTimeOffset(ExpiryDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var ago = (DateTimeOffset.UtcNow - expiryAt).HumanizeAr();
            return Result.Failure(new Error("Warranty.Expired", $"Warranty expired منذ {ago}."));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return Result.Failure(new Error("Warranty.ClaimDescriptionRequired", "Claim description is required."));
        }

        ClaimDate = claimDate;
        ClaimDescription = description.Trim();
        Status = WarrantyStatus.Claimed;
        Touch();
        return Result.Success();
    }

    public Result Process(string resolution, Guid? replacementSkuId, Guid processedBy)
    {
        if (Status != WarrantyStatus.Claimed)
        {
            return Result.Failure(new Error("Warranty.InvalidState", "Only claimed warranty records can be processed."));
        }

        if (string.IsNullOrWhiteSpace(resolution))
        {
            return Result.Failure(new Error("Warranty.ResolutionRequired", "Resolution is required."));
        }

        Resolution = resolution.Trim();
        ReplacementSkuId = replacementSkuId;
        ProcessedBy = processedBy;
        ProcessedAt = DateTimeOffset.UtcNow;
        Status = WarrantyStatus.Active;
        UpdatedBy = processedBy;
        Touch();
        return Result.Success();
    }

    public Result Reject(string reason, Guid rejectedBy)
    {
        if (Status != WarrantyStatus.Claimed)
        {
            return Result.Failure(new Error("Warranty.InvalidState", "Only claimed warranty records can be rejected."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(new Error("Warranty.RejectReasonRequired", "Rejection reason is required."));
        }

        RejectionReason = reason.Trim();
        Status = WarrantyStatus.Rejected;
        ProcessedBy = rejectedBy;
        ProcessedAt = DateTimeOffset.UtcNow;
        UpdatedBy = rejectedBy;
        Touch();
        return Result.Success();
    }

    public bool IsExpired() => DateOnly.FromDateTime(DateTime.UtcNow) > ExpiryDate;
}
