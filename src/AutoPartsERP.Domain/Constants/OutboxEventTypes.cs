namespace AutoPartsERP.Domain.Constants;

public static class OutboxEventTypes
{
    public const string InvoicePosted = "InvoicePosted";
    public const string PaymentAllocated = "PaymentAllocated";
    public const string InvoiceVoided = "InvoiceVoided";
    public const string PaymentReversed = "PaymentReversed";
    public const string BatchReceived = "BatchReceived";
    public const string StockAdjusted = "StockAdjusted";
    public const string WarrantyClaimCreated = "WarrantyClaimCreated";
    public const string WarrantyProcessed = "WarrantyProcessed";
    public const string PartyTypeActivated = "PartyTypeActivated";
    public const string TransferShipped = "TransferShipped";
}
