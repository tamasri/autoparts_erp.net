using Dapper;
using Humanizer;

namespace AutoPartsERP.Application.Features.Reports.GetBatchTrace;

public sealed record GetBatchTraceQuery(Guid BatchId)
    : IRequest<Result<IReadOnlyCollection<BatchTraceRowDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Reports.BatchTrace;
}

public sealed class GetBatchTraceQueryValidator : AbstractValidator<GetBatchTraceQuery>
{
    public GetBatchTraceQueryValidator()
    {
        RuleFor(x => x.BatchId).NotEmpty();
    }
}

public sealed class GetBatchTraceQueryHandler : IRequestHandler<GetBatchTraceQuery, Result<IReadOnlyCollection<BatchTraceRowDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetBatchTraceQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<IReadOnlyCollection<BatchTraceRowDto>>> Handle(GetBatchTraceQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<BatchTraceRowDto>(
            new CommandDefinition(
                """
                SELECT
                    bm.batch_id AS BatchId,
                    b.batch_number AS BatchNumber,
                    bm.movement_type AS MovementType,
                    bm.quantity AS Quantity,
                    bm.direction AS Direction,
                    bm.created_at AS CreatedAt,
                    bm.movement_type AS MovementTypeDisplay,
                    bm.created_at AS TimeAgo
                FROM batch_movements bm
                INNER JOIN batches b ON b.id = bm.batch_id
                WHERE bm.batch_id = @BatchId
                ORDER BY bm.created_at DESC;
                """,
                new { request.BatchId },
                cancellationToken: cancellationToken))).ToArray();

        var mapped = rows.Select(row => row with
        {
            MovementTypeDisplay = row.MovementType.Humanize(LetterCasing.Title),
            TimeAgo = ReportMappings.TimeAgo(row.CreatedAt)
        }).ToArray();

        return Result<IReadOnlyCollection<BatchTraceRowDto>>.Success(mapped);
    }
}
