namespace AutoPartsERP.Application.Features.Kpi.CreateKpiDefinition;

public sealed record CreateKpiDefinitionCommand(CreateKpiDefinitionRequest Request)
    : IRequest<Result<KpiDefinitionDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.System.ConfigWrite;
    public string AuditModule => "KPI";
}

public sealed class CreateKpiDefinitionCommandValidator : AbstractValidator<CreateKpiDefinitionCommand>
{
    public CreateKpiDefinitionCommandValidator()
    {
        RuleFor(x => x.Request.Key).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.Domain).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.TitleAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.Unit).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Direction).NotEmpty().Must(x => x.Equals("UP", StringComparison.OrdinalIgnoreCase) || x.Equals("DOWN", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class CreateKpiDefinitionCommandHandler : IRequestHandler<CreateKpiDefinitionCommand, Result<KpiDefinitionDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreateKpiDefinitionCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<KpiDefinitionDto>> Handle(CreateKpiDefinitionCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<KpiDefinitionRow>(new CommandDefinition(
            """
            INSERT INTO kpi_definitions (
                id, key, domain, title, title_ar, unit, direction, description, is_active, created_at, created_by)
            VALUES (
                @Id, @Key, @Domain, @Title, @TitleAr, @Unit, @Direction, @Description, TRUE, now(), @CreatedBy)
            ON CONFLICT (key) DO NOTHING
            RETURNING id AS Id, key AS Key, domain AS Domain, title AS Title, title_ar AS TitleAr,
                      unit AS Unit, direction AS Direction, description AS Description,
                      is_active AS IsActive, created_at AS CreatedAt;
            """,
            new
            {
                Id = Guid.NewGuid(),
                Key = request.Request.Key.Trim().ToLowerInvariant(),
                Domain = request.Request.Domain.Trim().ToUpperInvariant(),
                Title = request.Request.Title.Trim(),
                TitleAr = request.Request.TitleAr.Trim(),
                Unit = request.Request.Unit.Trim().ToUpperInvariant(),
                Direction = request.Request.Direction.Trim().ToUpperInvariant(),
                request.Request.Description,
                CreatedBy = _currentUser.UserId
            },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return Result<KpiDefinitionDto>.Failure(new Error("Kpi.Conflict", "KPI definition key already exists."));
        }

        return Result<KpiDefinitionDto>.Success(new KpiDefinitionDto(
            row.Id,
            row.Key,
            row.Domain,
            row.Title,
            row.TitleAr,
            row.Unit,
            row.Direction,
            row.Description,
            row.IsActive,
            row.CreatedAt));
    }

    private sealed class KpiDefinitionRow
    {
        public Guid Id { get; init; }
        public string Key { get; init; } = string.Empty;
        public string Domain { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string TitleAr { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public string Direction { get; init; } = string.Empty;
        public string? Description { get; init; }
        public bool IsActive { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }
}
