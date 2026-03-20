using Dapper;
using Humanizer;

namespace AutoPartsERP.Application.Features.Inventory.GetBatchMovements;

public sealed record GetBatchMovementsQuery(
    Guid BatchId,
    int PageNumber = 1,
    int PageSize = 50)
    : IRequest<Result<PagedResponse<BatchMovementDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Inventory.Read;
}

public sealed class GetBatchMovementsQueryValidator : AbstractValidator<GetBatchMovementsQuery>
{
    public GetBatchMovementsQueryValidator()
    {
        RuleFor(x => x.BatchId).NotEmpty();
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class GetBatchMovementsQueryHandler : IRequestHandler<GetBatchMovementsQuery, Result<PagedResponse<BatchMovementDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetBatchMovementsQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<BatchMovementDto>>> Handle(GetBatchMovementsQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var parameters = new DynamicParameters();
        parameters.Add("BatchId", request.BatchId);
        parameters.Add("Offset", (request.PageNumber - 1) * request.PageSize);
        parameters.Add("PageSize", request.PageSize);

        var items = (await connection.QueryAsync<BatchMovementDto>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    batch_id AS BatchId,
                    movement_type AS MovementType,
                    quantity AS Quantity,
                    direction AS Direction,
                    reference_type AS ReferenceType,
                    reference_id AS ReferenceId,
                    created_at AS CreatedAt,
                    movement_type AS MovementTypeDisplay,
                    created_at AS TimeAgo
                FROM batch_movements
                WHERE batch_id = @BatchId
                ORDER BY created_at DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """,
                parameters,
                cancellationToken: cancellationToken))).ToArray();

        var mapped = items.Select(item => item with
        {
            MovementTypeDisplay = item.MovementType.Humanize(LetterCasing.Title),
            TimeAgo = (DateTimeOffset.UtcNow - item.CreatedAt).Humanize(culture: new System.Globalization.CultureInfo("ar"))
        }).ToArray();

        var total = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM batch_movements WHERE batch_id = @BatchId;",
                new { request.BatchId },
                cancellationToken: cancellationToken));

        return Result<PagedResponse<BatchMovementDto>>.Success(new PagedResponse<BatchMovementDto>(mapped, request.PageNumber, request.PageSize, total));
    }
}
