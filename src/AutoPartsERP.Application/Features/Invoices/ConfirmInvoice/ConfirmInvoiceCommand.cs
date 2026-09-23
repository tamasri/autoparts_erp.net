using Dapper;

namespace AutoPartsERP.Application.Features.Invoices.ConfirmInvoice;

public sealed record ConfirmInvoiceCommand(Guid InvoiceId, string IdempotencyKey)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.Update;
    public string AuditModule => "INVOICES";
}

public sealed class ConfirmInvoiceCommandValidator : AbstractValidator<ConfirmInvoiceCommand>
{
    public ConfirmInvoiceCommandValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty();
    }
}

public sealed class ConfirmInvoiceCommandHandler : IRequestHandler<ConfirmInvoiceCommand, Result<Guid>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IPeriodLockService _periodLock;

    public ConfirmInvoiceCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, IPeriodLockService periodLock)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _periodLock = periodLock;
    }

    public async Task<Result<Guid>> Handle(ConfirmInvoiceCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var open = await InvoicePeriod.EnsureOpenAsync(_periodLock, connection, null, request.InvoiceId, cancellationToken);
        if (open.IsFailure)
        {
            return Result<Guid>.Failure(open.Error);
        }

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE invoices
            SET status = 'CONFIRMED',
                updated_at = now(),
                updated_by = @UpdatedBy
            WHERE id = @InvoiceId
              AND status = 'DRAFT';
            """,
            new { request.InvoiceId, UpdatedBy = _currentUser.UserId },
            cancellationToken: cancellationToken));

        return updated == 0
            ? Result<Guid>.Failure(new Error("Invoice.InvalidState", "Only draft invoices can be confirmed."))
            : Result<Guid>.Success(request.InvoiceId);
    }
}
