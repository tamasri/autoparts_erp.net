namespace AutoPartsERP.Application.Features.Kpi.SetKpiThreshold;

public sealed record SetKpiThresholdCommand(
    Guid KpiDefinitionId,
    SetKpiThresholdRequest Request,
    string IdempotencyKey)
    : IRequest<Result<KpiThresholdDto>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.System.ConfigWrite;
    public string AuditModule => "KPI";
    public bool RequiresApproval => true;
}

public sealed class SetKpiThresholdCommandValidator : AbstractValidator<SetKpiThresholdCommand>
{
    public SetKpiThresholdCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.KpiDefinitionId).NotEmpty();
        RuleFor(x => x.Request.EffectiveFrom).NotEmpty();
        RuleFor(x => x.Request).Must(x => !x.EffectiveTo.HasValue || x.EffectiveTo.Value >= x.EffectiveFrom)
            .WithMessage("Effective date range is invalid.");
    }
}

public sealed class SetKpiThresholdCommandHandler : IRequestHandler<SetKpiThresholdCommand, Result<KpiThresholdDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public SetKpiThresholdCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<KpiThresholdDto>> Handle(SetKpiThresholdCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<KpiThresholdDto>(new CommandDefinition(
            """
            INSERT INTO kpi_thresholds (
                id, kpi_definition_id, warning_value, critical_value, effective_from, effective_to, set_by, created_at)
            VALUES (
                @Id, @KpiDefinitionId, @WarningValue, @CriticalValue, @EffectiveFrom, @EffectiveTo, @SetBy, now())
            RETURNING id AS Id, kpi_definition_id AS KpiDefinitionId, warning_value AS WarningValue,
                      critical_value AS CriticalValue, effective_from AS EffectiveFrom, effective_to AS EffectiveTo,
                      set_by AS SetBy, created_at AS CreatedAt;
            """,
            new
            {
                Id = Guid.NewGuid(),
                request.KpiDefinitionId,
                request.Request.WarningValue,
                request.Request.CriticalValue,
                request.Request.EffectiveFrom,
                request.Request.EffectiveTo,
                SetBy = _currentUser.UserId
            },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return Result<KpiThresholdDto>.Failure(new Error("Kpi.ThresholdFailed", "Threshold could not be stored."));
        }

        return Result<KpiThresholdDto>.Success(row);
    }
}
