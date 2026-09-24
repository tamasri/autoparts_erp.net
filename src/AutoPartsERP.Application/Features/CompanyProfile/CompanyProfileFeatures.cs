namespace AutoPartsERP.Application.Features.CompanyProfile;

// The company's own details printed on every document. One row; read by every printed form, edited by whoever holds
// system:config_write (the settings screen "بيانات المنشأة").

/// <summary>Reads the company profile (or the empty default when the row is missing).</summary>
public static class CompanyProfiles
{
    private const string Select = """
        SELECT name AS Name, manager_name AS ManagerName, address AS Address, city AS City, phone AS Phone, email AS Email,
               website AS Website, tax_number AS TaxNumber, whatsapp AS WhatsApp, bank_name AS BankName, bank_account AS BankAccount,
               iban AS Iban, invoice_subtitle AS InvoiceSubtitle, invoice_terms AS InvoiceTerms, receipt_note AS ReceiptNote,
               statement_note AS StatementNote, updated_at AS UpdatedAt
        FROM company_profile WHERE id = 1;
        """;

    public static async Task<CompanyProfileDto> LoadAsync(DbConnection connection, CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<CompanyProfileDto>(new CommandDefinition(Select, cancellationToken: cancellationToken))
        ?? CompanyProfileDto.Empty;

    public static async Task<CompanyProfileDto> LoadAsync(IDbConnectionFactory factory, CancellationToken cancellationToken)
    {
        await using var connection = await factory.CreateOpenConnectionAsync(cancellationToken);
        return await LoadAsync(connection, cancellationToken);
    }

    /// <summary>The invoice terms as separate lines (blank lines dropped).</summary>
    public static IReadOnlyList<string> Terms(CompanyProfileDto company) =>
        (company.InvoiceTerms ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

// ------------------------------------------------------------------ read

/// <summary>Any signed-in user may read it: it is printed on every document they can print anyway.</summary>
public sealed record GetCompanyProfileQuery : IRequest<Result<CompanyProfileDto>>;

public sealed class GetCompanyProfileQueryHandler : IRequestHandler<GetCompanyProfileQuery, Result<CompanyProfileDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetCompanyProfileQueryHandler(IDbConnectionFactory connectionFactory) { _connectionFactory = connectionFactory; }

    public async Task<Result<CompanyProfileDto>> Handle(GetCompanyProfileQuery request, CancellationToken cancellationToken) =>
        Result<CompanyProfileDto>.Success(await CompanyProfiles.LoadAsync(_connectionFactory, cancellationToken));
}

// ------------------------------------------------------------------ update

public sealed record UpdateCompanyProfileCommand(CompanyProfileDto Profile)
    : IRequest<Result<CompanyProfileDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.System.ConfigWrite;
    public string AuditModule => "SETTINGS";
}

public sealed class UpdateCompanyProfileCommandValidator : AbstractValidator<UpdateCompanyProfileCommand>
{
    public UpdateCompanyProfileCommandValidator()
    {
        RuleFor(x => x.Profile).NotNull();
        RuleFor(x => x.Profile.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Profile.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Profile.Email));
        RuleFor(x => x.Profile.Iban).Matches("^[A-Za-z]{2}[0-9]{2}[A-Za-z0-9 ]{8,34}$").When(x => !string.IsNullOrWhiteSpace(x.Profile.Iban))
            .WithMessage("IBAN must start with a country code and two check digits.");
        RuleFor(x => x.Profile.WhatsApp).Matches(@"^\+?[0-9 ()-]{6,20}$").When(x => !string.IsNullOrWhiteSpace(x.Profile.WhatsApp));
        foreach (var (field, max) in new (Func<CompanyProfileDto, string?> Field, int Max)[]
                 {
                     (p => p.ManagerName, 200), (p => p.Address, 300), (p => p.City, 100), (p => p.Phone, 50), (p => p.Website, 200),
                     (p => p.TaxNumber, 50), (p => p.BankName, 150), (p => p.BankAccount, 60), (p => p.InvoiceSubtitle, 200),
                     (p => p.InvoiceTerms, 3000), (p => p.ReceiptNote, 500), (p => p.StatementNote, 500),
                 })
        {
            RuleFor(x => field(x.Profile)).MaximumLength(max);
        }
    }
}

public sealed class UpdateCompanyProfileCommandHandler : IRequestHandler<UpdateCompanyProfileCommand, Result<CompanyProfileDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public UpdateCompanyProfileCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<CompanyProfileDto>> Handle(UpdateCompanyProfileCommand request, CancellationToken cancellationToken)
    {
        static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        var p = request.Profile;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO company_profile (id, name, manager_name, address, city, phone, email, website, tax_number, whatsapp, bank_name,
                                         bank_account, iban, invoice_subtitle, invoice_terms, receipt_note, statement_note, updated_at, updated_by)
            VALUES (1, @Name, @ManagerName, @Address, @City, @Phone, @Email, @Website, @TaxNumber, @WhatsApp, @BankName,
                    @BankAccount, @Iban, @InvoiceSubtitle, @InvoiceTerms, @ReceiptNote, @StatementNote, now(), @UserId)
            ON CONFLICT (id) DO UPDATE SET
                name = EXCLUDED.name, manager_name = EXCLUDED.manager_name, address = EXCLUDED.address, city = EXCLUDED.city,
                phone = EXCLUDED.phone, email = EXCLUDED.email, website = EXCLUDED.website, tax_number = EXCLUDED.tax_number,
                whatsapp = EXCLUDED.whatsapp, bank_name = EXCLUDED.bank_name, bank_account = EXCLUDED.bank_account, iban = EXCLUDED.iban,
                invoice_subtitle = EXCLUDED.invoice_subtitle, invoice_terms = EXCLUDED.invoice_terms, receipt_note = EXCLUDED.receipt_note,
                statement_note = EXCLUDED.statement_note, updated_at = now(), updated_by = EXCLUDED.updated_by;
            """,
            new
            {
                Name = p.Name.Trim(), ManagerName = Clean(p.ManagerName), Address = Clean(p.Address), City = Clean(p.City), Phone = Clean(p.Phone),
                Email = Clean(p.Email), Website = Clean(p.Website), TaxNumber = Clean(p.TaxNumber), WhatsApp = Clean(p.WhatsApp),
                BankName = Clean(p.BankName), BankAccount = Clean(p.BankAccount), Iban = Clean(p.Iban)?.Replace(" ", string.Empty).ToUpperInvariant(),
                InvoiceSubtitle = Clean(p.InvoiceSubtitle), InvoiceTerms = Clean(p.InvoiceTerms?.Replace("\r\n", "\n")),
                ReceiptNote = Clean(p.ReceiptNote), StatementNote = Clean(p.StatementNote), UserId = _currentUser.UserId,
            },
            cancellationToken: cancellationToken));

        return Result<CompanyProfileDto>.Success(await CompanyProfiles.LoadAsync(connection, cancellationToken));
    }
}
