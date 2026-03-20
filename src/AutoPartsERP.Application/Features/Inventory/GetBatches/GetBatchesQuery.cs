using Dapper;
using AutoPartsERP.Domain.Extensions;
using Humanizer;

namespace AutoPartsERP.Application.Features.Inventory.GetBatches;

public sealed record GetBatchesQuery(
    int PageNumber = 1,
    int PageSize = 20,
    Guid? SkuId = null,
    Guid? LocationId = null,
    string? Status = null)
    : IRequest<Result<PagedResponse<BatchDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Inventory.ViewBatches;
}

public sealed class GetBatchesQueryValidator : AbstractValidator<GetBatchesQuery>
{
    public GetBatchesQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class GetBatchesQueryHandler : IRequestHandler<GetBatchesQuery, Result<PagedResponse<BatchDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetBatchesQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<BatchDto>>> Handle(GetBatchesQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (request.SkuId.HasValue)
        {
            conditions.Add("b.sku_id = @SkuId");
            parameters.Add("SkuId", request.SkuId.Value);
        }

        if (request.LocationId.HasValue)
        {
            conditions.Add("b.location_id = @LocationId");
            parameters.Add("LocationId", request.LocationId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            conditions.Add("b.status = @Status");
            parameters.Add("Status", request.Status.Trim().ToUpperInvariant());
        }

        var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
        parameters.Add("Offset", (request.PageNumber - 1) * request.PageSize);
        parameters.Add("PageSize", request.PageSize);

        var rows = (await connection.QueryAsync<BatchRow>(
            new CommandDefinition($"""
                SELECT
                    b.id AS Id,
                    b.batch_number AS BatchNumber,
                    b.sku_id AS SkuId,
                    b.location_id AS LocationId,
                    b.quantity_initial AS QuantityInitial,
                    b.quantity_current AS QuantityCurrent,
                    b.cost_price_syp AS CostPriceSyp,
                    b.cost_price_usd AS CostPriceUsd,
                    b.received_date AS ReceivedDate,
                    b.expiry_date AS ExpiryDate,
                    b.status AS Status,
                    b.received_date::timestamp - now() AS BatchAgeSpan,
                    b.expiry_date::timestamp - now() AS ExpirySpan
                FROM batches b
                {where}
                ORDER BY b.received_date DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """,
                parameters,
                cancellationToken: cancellationToken))).ToArray();

        var items = rows.Select(row =>
        {
            var receivedDate = row.ReceivedDate;
            var expiryDate = row.ExpiryDate;
            var batchAge = (DateTime.UtcNow - receivedDate).Humanize(culture: new System.Globalization.CultureInfo("ar"));
            var expiryDisplay = expiryDate is null
                ? "غير محدد"
                : ((expiryDate.Value < DateTime.UtcNow)
                    ? $"انتهى منذ {(DateTime.UtcNow - expiryDate.Value).Humanize(culture: new System.Globalization.CultureInfo("ar"))}"
                    : $"ينتهي {expiryDate.Value.Humanize(culture: new System.Globalization.CultureInfo("ar"))}");

            return new BatchDto(
                row.Id,
                row.BatchNumber,
                row.SkuId,
                row.LocationId,
                row.QuantityInitial,
                row.QuantityCurrent,
                row.CostPriceSyp,
                row.CostPriceUsd,
                DateOnly.FromDateTime(receivedDate),
                expiryDate is null ? null : DateOnly.FromDateTime(expiryDate.Value),
                row.Status,
                batchAge,
                expiryDisplay);
        }).ToArray();

        var total = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition($"""
                SELECT COUNT(*)
                FROM batches b
                {where};
                """,
                parameters,
                cancellationToken: cancellationToken));

        return Result<PagedResponse<BatchDto>>.Success(new PagedResponse<BatchDto>(items, request.PageNumber, request.PageSize, total));
    }
}

internal sealed record BatchRow(
    Guid Id,
    string BatchNumber,
    Guid SkuId,
    Guid LocationId,
    decimal QuantityInitial,
    decimal QuantityCurrent,
    decimal CostPriceSyp,
    decimal CostPriceUsd,
    DateTime ReceivedDate,
    DateTime? ExpiryDate,
    string Status);
