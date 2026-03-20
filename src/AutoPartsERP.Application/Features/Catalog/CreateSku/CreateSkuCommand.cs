namespace AutoPartsERP.Application.Features.Catalog.CreateSku;

public sealed record CreateSkuCommand(CreateSkuRequest Request, string IdempotencyKey)
    : IRequest<Result<SkuDto>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Catalog.Write;
    public string AuditModule => "CATALOG";
}

public sealed class CreateSkuCommandValidator : AbstractValidator<CreateSkuCommand>
{
    public CreateSkuCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Request.NameAr).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Request.SellingPriceSyp).GreaterThanOrEqualTo(x => x.Request.MinSellingPriceSyp);
        RuleFor(x => x.Request.SellingPriceUsd).GreaterThanOrEqualTo(x => x.Request.MinSellingPriceUsd);
        RuleFor(x => x.Request.WarrantyMonths).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request).Must(r => !r.HasWarranty || r.WarrantyMonths > 0).WithMessage("Warranty months must be greater than zero when warranty is enabled.");
    }
}

public sealed class CreateSkuCommandHandler : IRequestHandler<CreateSkuCommand, Result<SkuDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreateSkuCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<SkuDto>> Handle(CreateSkuCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        if (!await CategoryExistsAsync(connection, request.Request.CategoryId, cancellationToken))
        {
            return Result<SkuDto>.Failure(new Error("Sku.CategoryNotFound", "Category was not found."));
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO skus
                (id, code, name, name_ar, category_id, barcode, selling_price_syp, selling_price_usd,
                 min_selling_price_syp, min_selling_price_usd, is_batch_tracked, has_warranty,
                 warranty_months, tags, created_by)
            VALUES
                (@Id, @Code, @Name, @NameAr, @CategoryId, @Barcode, @SellingPriceSyp, @SellingPriceUsd,
                 @MinSellingPriceSyp, @MinSellingPriceUsd, @IsBatchTracked, @HasWarranty,
                 @WarrantyMonths, @Tags, @CreatedBy)
            RETURNING id, code, name, name_ar, category_id, barcode, is_batch_tracked, has_warranty,
                      warranty_months, selling_price_syp, selling_price_usd, min_selling_price_syp,
                      min_selling_price_usd, is_active, tags;
            """;

        AddParameter(command, "Id", Guid.NewGuid());
        AddParameter(command, "Code", request.Request.Code.Trim().ToUpperInvariant());
        AddParameter(command, "Name", request.Request.Name.Trim());
        AddParameter(command, "NameAr", request.Request.NameAr.Trim());
        AddParameter(command, "CategoryId", request.Request.CategoryId);
        AddParameter(command, "Barcode", (object?)request.Request.Barcode?.Trim() ?? DBNull.Value);
        AddParameter(command, "SellingPriceSyp", request.Request.SellingPriceSyp);
        AddParameter(command, "SellingPriceUsd", request.Request.SellingPriceUsd);
        AddParameter(command, "MinSellingPriceSyp", request.Request.MinSellingPriceSyp);
        AddParameter(command, "MinSellingPriceUsd", request.Request.MinSellingPriceUsd);
        AddParameter(command, "IsBatchTracked", request.Request.IsBatchTracked);
        AddParameter(command, "HasWarranty", request.Request.HasWarranty);
        AddParameter(command, "WarrantyMonths", request.Request.WarrantyMonths);
        AddParameter(command, "Tags", request.Request.Tags ?? Array.Empty<string>());
        AddParameter(command, "CreatedBy", _currentUser.UserId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result<SkuDto>.Failure(new Error("Sku.CreateFailed", "SKU could not be created."));
        }

        return Result<SkuDto>.Success(MapSku(reader));
    }

    private static async Task<bool> CategoryExistsAsync(DbConnection connection, Guid categoryId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM categories WHERE id = @Id;";
        AddParameter(command, "Id", categoryId);
        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar is not null and not DBNull;
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
