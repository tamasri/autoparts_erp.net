namespace AutoPartsERP.Application.Features.Catalog.UpdateSkuPrices;

public sealed record UpdateSkuPricesCommand(Guid SkuId, UpdateSkuPricesRequest Request)
    : IRequest<Result<SkuDto>>, IAuthorizedRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.Catalog.Write;
    public string AuditModule => "CATALOG";
    public bool RequiresApproval => Request.RequiresApproval;
}

public sealed class UpdateSkuPricesCommandValidator : AbstractValidator<UpdateSkuPricesCommand>
{
    public UpdateSkuPricesCommandValidator()
    {
        RuleFor(x => x.SkuId).NotEmpty();
        RuleFor(x => x.Request.SellingPriceSyp).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.SellingPriceUsd).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.MinSellingPriceSyp).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.MinSellingPriceUsd).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request).Must(r => !string.IsNullOrWhiteSpace(r.OverrideReason) || r.SellingPriceSyp >= r.MinSellingPriceSyp && r.SellingPriceUsd >= r.MinSellingPriceUsd)
            .WithMessage("Price override reason is required when selling price is below minimum.");
    }
}

public sealed class UpdateSkuPricesCommandHandler : IRequestHandler<UpdateSkuPricesCommand, Result<SkuDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public UpdateSkuPricesCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<SkuDto>> Handle(UpdateSkuPricesCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE skus
            SET selling_price_syp = @SellingPriceSyp,
                selling_price_usd = @SellingPriceUsd,
                min_selling_price_syp = @MinSellingPriceSyp,
                min_selling_price_usd = @MinSellingPriceUsd,
                updated_at = now(),
                updated_by = @UpdatedBy
            WHERE id = @Id
            RETURNING id, code, name, name_ar, category_id, barcode, is_batch_tracked, has_warranty,
                      warranty_months, selling_price_syp, selling_price_usd, min_selling_price_syp,
                      min_selling_price_usd, is_active, tags;
            """;

        AddParameter(command, "Id", request.SkuId);
        AddParameter(command, "SellingPriceSyp", request.Request.SellingPriceSyp);
        AddParameter(command, "SellingPriceUsd", request.Request.SellingPriceUsd);
        AddParameter(command, "MinSellingPriceSyp", request.Request.MinSellingPriceSyp);
        AddParameter(command, "MinSellingPriceUsd", request.Request.MinSellingPriceUsd);
        AddParameter(command, "UpdatedBy", _currentUser.UserId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result<SkuDto>.Failure(new Error("Sku.NotFound", "SKU was not found."));
        }

        return Result<SkuDto>.Success(MapSku(reader));
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static SkuDto MapSku(DbDataReader reader)
    {
        return new SkuDto(
            reader.GetGuid(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("code")),
            reader.GetString(reader.GetOrdinal("name")),
            reader.GetString(reader.GetOrdinal("name_ar")),
            reader.GetGuid(reader.GetOrdinal("category_id")),
            reader.IsDBNull(reader.GetOrdinal("barcode")) ? null : reader.GetString(reader.GetOrdinal("barcode")),
            reader.GetBoolean(reader.GetOrdinal("is_batch_tracked")),
            reader.GetBoolean(reader.GetOrdinal("has_warranty")),
            reader.GetInt32(reader.GetOrdinal("warranty_months")),
            reader.GetDecimal(reader.GetOrdinal("selling_price_syp")),
            reader.GetDecimal(reader.GetOrdinal("selling_price_usd")),
            reader.GetDecimal(reader.GetOrdinal("min_selling_price_syp")),
            reader.GetDecimal(reader.GetOrdinal("min_selling_price_usd")),
            reader.GetBoolean(reader.GetOrdinal("is_active")),
            reader.IsDBNull(reader.GetOrdinal("tags")) ? Array.Empty<string>() : (string[])reader["tags"]);
    }
}
