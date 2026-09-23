namespace AutoPartsERP.Application.Features.Invoices.SetInvoiceDiscount;

/// <summary>Sets or clears the discount on a whole draft invoice (lines keep their own discounts).</summary>
public sealed record SetInvoiceDiscountCommand(Guid InvoiceId, decimal? DiscountPct, decimal? DiscountAmountUsd)
    : IRequest<Result<InvoiceDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.Update;
    public string AuditModule => "INVOICES";
}

public sealed class SetInvoiceDiscountCommandValidator : AbstractValidator<SetInvoiceDiscountCommand>
{
    public SetInvoiceDiscountCommandValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x).Must(x => x.DiscountPct is null || x.DiscountAmountUsd is null).WithMessage("Give the invoice discount as a percentage or as an amount, not both.");
        RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100).When(x => x.DiscountPct is not null);
        RuleFor(x => x.DiscountAmountUsd).GreaterThanOrEqualTo(0).When(x => x.DiscountAmountUsd is not null);
    }
}

public sealed class SetInvoiceDiscountCommandHandler : IRequestHandler<SetInvoiceDiscountCommand, Result<InvoiceDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPeriodLockService _periodLock;

    public SetInvoiceDiscountCommandHandler(IDbConnectionFactory connectionFactory, IPeriodLockService periodLock)
    {
        _connectionFactory = connectionFactory;
        _periodLock = periodLock;
    }

    public async Task<Result<InvoiceDto>> Handle(SetInvoiceDiscountCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var status = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT status FROM invoices WHERE id = @InvoiceId FOR UPDATE;", new { request.InvoiceId }, transaction, cancellationToken: cancellationToken));
        if (status is null)
        {
            return Result<InvoiceDto>.Failure(new Error("Invoice.NotFound", "Invoice was not found."));
        }

        if (status != "DRAFT")
        {
            return Result<InvoiceDto>.Failure(new Error("Invoice.InvalidState", "The discount can only be changed on a draft invoice."));
        }

        var open = await InvoicePeriod.EnsureOpenAsync(_periodLock, connection, transaction, request.InvoiceId, cancellationToken);
        if (open.IsFailure)
        {
            return Result<InvoiceDto>.Failure(open.Error);
        }

        var applied = await InvoiceTotals.ApplyDiscountAsync(connection, transaction, request.InvoiceId, request.DiscountPct, request.DiscountAmountUsd, cancellationToken);
        if (applied.IsFailure)
        {
            return Result<InvoiceDto>.Failure(applied.Error);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result<InvoiceDto>.Success((await InvoiceReader.LoadAsync(connection, null, request.InvoiceId, cancellationToken))!);
    }
}
