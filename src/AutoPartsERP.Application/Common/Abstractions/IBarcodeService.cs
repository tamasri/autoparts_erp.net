namespace AutoPartsERP.Application.Common.Abstractions;

public interface IBarcodeService
{
    byte[] GenerateItemCodePng(Guid itemId, string partNumberCanonical);
    byte[] GenerateBatchCodePng(Guid batchId, string batchNumber);
}

