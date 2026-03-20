using System.Globalization;
using AutoPartsERP.Domain.Extensions;
using Humanizer;

namespace AutoPartsERP.Application.Features.Warranty;

internal static class WarrantyMappings
{
    private static readonly CultureInfo Arabic = new("ar");

    public static string GetExpiryDisplay(DateOnly expiryDate)
    {
        var expiry = expiryDate.ToDateTime(TimeOnly.MinValue);
        return expiryDate < DateOnly.FromDateTime(DateTime.UtcNow)
            ? $"انتهى منذ {(DateTime.UtcNow - expiry).Humanize(culture: Arabic)}"
            : $"ينتهي {expiry.Humanize(culture: Arabic)}";
    }

    public static WarrantyRecordDto ToDto(
        Guid id,
        string warrantyNumber,
        Guid invoiceLineId,
        Guid skuId,
        string skuCode,
        string skuName,
        Guid? batchId,
        Guid customerId,
        string customerName,
        DateOnly saleDate,
        DateOnly expiryDate,
        DateOnly? claimDate,
        string status,
        string? claimDescription,
        string? resolution,
        string? rejectionReason)
    {
        return new WarrantyRecordDto(
            id,
            warrantyNumber,
            invoiceLineId,
            skuId,
            skuCode,
            skuName,
            batchId,
            customerId,
            customerName,
            saleDate,
            expiryDate,
            claimDate,
            status,
            claimDescription,
            resolution,
            rejectionReason,
            status.Humanize(LetterCasing.Title),
            GetExpiryDisplay(expiryDate));
    }
}
