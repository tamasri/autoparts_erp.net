using AutoPartsERP.Application.Common.Abstractions;
using QRCoder;

namespace AutoPartsERP.Infrastructure.Services;

public sealed class BarcodeService : IBarcodeService
{
    public byte[] GenerateItemCodePng(Guid itemId, string partNumberCanonical)
    {
        var payload = $"ITEM|{itemId}|{partNumberCanonical}";
        return GenerateQr(payload);
    }

    public byte[] GenerateBatchCodePng(Guid batchId, string batchNumber)
    {
        var payload = $"BATCH|{batchId}|{batchNumber}";
        return GenerateQr(payload);
    }

    private static byte[] GenerateQr(string payload)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(qrData);
        return png.GetGraphic(20);
    }
}

