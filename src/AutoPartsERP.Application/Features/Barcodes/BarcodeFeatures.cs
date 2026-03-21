namespace AutoPartsERP.Application.Features.Barcodes;

public sealed record ScanBarcodeCommand(BarcodeScanRequest Request)
    : IRequest<Result<BarcodeScanResultDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Barcodes.Scan;
    public string AuditModule => "INVENTORY";
}

public sealed class ScanBarcodeCommandValidator : AbstractValidator<ScanBarcodeCommand>
{
    public ScanBarcodeCommandValidator()
    {
        RuleFor(x => x.Request.ScanCode).NotEmpty().MaximumLength(240);
        RuleFor(x => x.Request.ScanType).NotEmpty().MaximumLength(32);
    }
}

public sealed class ScanBarcodeCommandHandler : IRequestHandler<ScanBarcodeCommand, Result<BarcodeScanResultDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public ScanBarcodeCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<BarcodeScanResultDto>> Handle(ScanBarcodeCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        Guid? itemId = null;
        Guid? batchId = null;
        string? itemPart = null;
        string? itemNameAr = null;
        decimal? availableQty = null;
        string message = "لم يتم العثور على الصنف.";

        var item = await connection.QuerySingleOrDefaultAsync<(Guid Id, string PartNumberCanonical, string NameAr)>(
            new CommandDefinition(
                """
                SELECT i.id AS Id, i.part_number_canonical AS PartNumberCanonical, i.name_ar AS NameAr
                FROM items i
                INNER JOIN skus s ON s.id = i.sku_id
                WHERE s.barcode = @Barcode
                LIMIT 1;
                """,
                new { Barcode = request.Request.ScanCode.Trim() },
                cancellationToken: cancellationToken));

        if (item.Id != Guid.Empty)
        {
            itemId = item.Id;
            itemPart = item.PartNumberCanonical;
            itemNameAr = item.NameAr;

            availableQty = await connection.QuerySingleOrDefaultAsync<decimal>(
                new CommandDefinition(
                    """
                    SELECT COALESCE(SUM(qty), 0)
                    FROM inventory_balances
                    WHERE item_id = @ItemId
                      AND status = 'AVAILABLE';
                    """,
                    new { ItemId = item.Id },
                    cancellationToken: cancellationToken));

            message = "تم العثور على الصنف.";
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO barcode_scan_logs (
                id, scan_code, scan_type, item_id, batch_id, location_id, scanned_by, scanned_at, device_id)
            VALUES (
                @Id, @ScanCode, @ScanType, @ItemId, @BatchId, @LocationId, @ScannedBy, now(), @DeviceId);
            """,
            new
            {
                Id = Guid.NewGuid(),
                ScanCode = request.Request.ScanCode.Trim(),
                ScanType = request.Request.ScanType.Trim().ToUpperInvariant(),
                ItemId = itemId,
                BatchId = batchId,
                request.Request.LocationId,
                ScannedBy = _currentUser.UserId,
                request.Request.DeviceId
            },
            cancellationToken: cancellationToken));

        return Result<BarcodeScanResultDto>.Success(new BarcodeScanResultDto(
            request.Request.ScanCode,
            request.Request.ScanType.Trim().ToUpperInvariant(),
            itemId,
            batchId,
            itemPart,
            itemNameAr,
            availableQty,
            message));
    }
}

