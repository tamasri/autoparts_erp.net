using Dapper;
using Humanizer;

namespace AutoPartsERP.Application.Features.Warranty.GetWarrantyRecords;

public sealed record GetWarrantyRecordsQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Status = null,
    Guid? CustomerId = null,
    Guid? SkuId = null)
    : IRequest<Result<PagedResponse<WarrantyRecordDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Warranty.Read;
}

public sealed class GetWarrantyRecordsQueryValidator : AbstractValidator<GetWarrantyRecordsQuery>
{
    public GetWarrantyRecordsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class GetWarrantyRecordsQueryHandler : IRequestHandler<GetWarrantyRecordsQuery, Result<PagedResponse<WarrantyRecordDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetWarrantyRecordsQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<WarrantyRecordDto>>> Handle(GetWarrantyRecordsQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            conditions.Add("w.status = @Status");
            parameters.Add("Status", request.Status.Trim().ToUpperInvariant());
        }

        if (request.CustomerId.HasValue)
        {
            conditions.Add("w.customer_id = @CustomerId");
            parameters.Add("CustomerId", request.CustomerId.Value);
        }

        if (request.SkuId.HasValue)
        {
            conditions.Add("w.sku_id = @SkuId");
            parameters.Add("SkuId", request.SkuId.Value);
        }

        var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
        parameters.Add("Offset", (request.PageNumber - 1) * request.PageSize);
        parameters.Add("PageSize", request.PageSize);

        var items = (await connection.QueryAsync<WarrantyRecordDto>(
            new CommandDefinition($"""
                SELECT
                    w.id AS Id,
                    w.warranty_number AS WarrantyNumber,
                    w.invoice_line_id AS InvoiceLineId,
                    w.sku_id AS SkuId,
                    s.code AS SkuCode,
                    s.name AS SkuName,
                    w.batch_id AS BatchId,
                    w.customer_id AS CustomerId,
                    c.name AS CustomerName,
                    w.sale_date AS SaleDate,
                    w.expiry_date AS ExpiryDate,
                    w.claim_date AS ClaimDate,
                    w.status AS Status,
                    w.claim_description AS ClaimDescription,
                    w.resolution AS Resolution,
                    w.rejection_reason AS RejectionReason,
                    w.status AS StatusDisplay,
                    w.expiry_date AS WarrantyExpiryDisplay
                FROM warranty_records w
                INNER JOIN skus s ON s.id = w.sku_id
                INNER JOIN customers c ON c.id = w.customer_id
                {where}
                ORDER BY w.created_at DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """,
                parameters,
                cancellationToken: cancellationToken))).ToArray();

        var mapped = items.Select(item => item with
        {
            StatusDisplay = item.Status.Humanize(LetterCasing.Title),
            WarrantyExpiryDisplay = WarrantyMappings.GetExpiryDisplay(item.ExpiryDate)
        }).ToArray();

        var total = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition($"""
                SELECT COUNT(*)
                FROM warranty_records w
                INNER JOIN skus s ON s.id = w.sku_id
                INNER JOIN customers c ON c.id = w.customer_id
                {where};
                """,
                parameters,
                cancellationToken: cancellationToken));

        return Result<PagedResponse<WarrantyRecordDto>>.Success(new PagedResponse<WarrantyRecordDto>(mapped, request.PageNumber, request.PageSize, total));
    }
}
