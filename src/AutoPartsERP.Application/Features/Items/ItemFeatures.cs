namespace AutoPartsERP.Application.Features.Items;

public sealed record SearchItemsQuery(SearchItemsRequest Request)
    : IRequest<Result<PagedResponse<ItemSearchResultDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Items.Read;
}

public sealed class SearchItemsQueryValidator : AbstractValidator<SearchItemsQuery>
{
    public SearchItemsQueryValidator()
    {
        RuleFor(x => x.Request.Query).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.PageNumber).GreaterThan(0);
        RuleFor(x => x.Request.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class SearchItemsQueryHandler : IRequestHandler<SearchItemsQuery, Result<PagedResponse<ItemSearchResultDto>>>
{
    private readonly IItemSearchService _itemSearchService;

    public SearchItemsQueryHandler(IItemSearchService itemSearchService)
    {
        _itemSearchService = itemSearchService;
    }

    public async Task<Result<PagedResponse<ItemSearchResultDto>>> Handle(SearchItemsQuery request, CancellationToken cancellationToken)
    {
        return await _itemSearchService.SearchAsync(request.Request, cancellationToken);
    }
}

public sealed record GetItemByIdQuery(Guid ItemId) : IRequest<Result<ItemDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Items.Read;
}

public sealed class GetItemByIdQueryHandler : IRequestHandler<GetItemByIdQuery, Result<ItemDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetItemByIdQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<ItemDto>> Handle(GetItemByIdQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var dto = await connection.QuerySingleOrDefaultAsync<ItemDto>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    sku_id AS SkuId,
                    part_number AS PartNumber,
                    part_number_canonical AS PartNumberCanonical,
                    part_number_numeric AS PartNumberNumeric,
                    name_en AS NameEn,
                    name_ar AS NameAr,
                    name_ar_colloquial AS NameArColloquial,
                    brand AS Brand,
                    category_path::text AS CategoryPath,
                    has_warranty AS HasWarranty,
                    warranty_months AS WarrantyMonths,
                    is_batch_tracked AS IsBatchTracked,
                    reorder_level AS ReorderLevel,
                    is_active AS IsActive,
                    is_stop_ship AS IsStopShip,
                    stop_ship_reason AS StopShipReason,
                    notes AS Notes
                FROM items
                WHERE id = @Id;
                """,
                new { Id = request.ItemId },
                cancellationToken: cancellationToken));

        if (dto is null)
        {
            return Result<ItemDto>.Failure(new Error("Item.NotFound", "Item was not found."));
        }

        return Result<ItemDto>.Success(dto);
    }
}

public sealed record GetItemByPartNumberQuery(string Number) : IRequest<Result<ItemDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Items.Read;
}

public sealed class GetItemByPartNumberQueryHandler : IRequestHandler<GetItemByPartNumberQuery, Result<ItemDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPartNumberService _partNumberService;

    public GetItemByPartNumberQueryHandler(IDbConnectionFactory connectionFactory, IPartNumberService partNumberService)
    {
        _connectionFactory = connectionFactory;
        _partNumberService = partNumberService;
    }

    public async Task<Result<ItemDto>> Handle(GetItemByPartNumberQuery request, CancellationToken cancellationToken)
    {
        var normalized = _partNumberService.NormalizePartNumber(request.Number);
        if (string.IsNullOrWhiteSpace(normalized.Canonical))
        {
            return Result<ItemDto>.Failure(new Error("Item.PartNumberInvalid", "Part number is invalid."));
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var dto = await connection.QuerySingleOrDefaultAsync<ItemDto>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    sku_id AS SkuId,
                    part_number AS PartNumber,
                    part_number_canonical AS PartNumberCanonical,
                    part_number_numeric AS PartNumberNumeric,
                    name_en AS NameEn,
                    name_ar AS NameAr,
                    name_ar_colloquial AS NameArColloquial,
                    brand AS Brand,
                    category_path::text AS CategoryPath,
                    has_warranty AS HasWarranty,
                    warranty_months AS WarrantyMonths,
                    is_batch_tracked AS IsBatchTracked,
                    reorder_level AS ReorderLevel,
                    is_active AS IsActive,
                    is_stop_ship AS IsStopShip,
                    stop_ship_reason AS StopShipReason,
                    notes AS Notes
                FROM items
                WHERE part_number_canonical = @Canonical
                LIMIT 1;
                """,
                new { Canonical = normalized.Canonical },
                cancellationToken: cancellationToken));

        if (dto is null)
        {
            return Result<ItemDto>.Failure(new Error("Item.NotFound", "Item was not found."));
        }

        return Result<ItemDto>.Success(dto);
    }
}

public sealed record GetItemByBarcodeQuery(string Barcode) : IRequest<Result<ItemDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Items.Read;
}

public sealed class GetItemByBarcodeQueryHandler : IRequestHandler<GetItemByBarcodeQuery, Result<ItemDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetItemByBarcodeQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<ItemDto>> Handle(GetItemByBarcodeQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var dto = await connection.QuerySingleOrDefaultAsync<ItemDto>(
            new CommandDefinition(
                """
                SELECT
                    i.id AS Id,
                    i.sku_id AS SkuId,
                    i.part_number AS PartNumber,
                    i.part_number_canonical AS PartNumberCanonical,
                    i.part_number_numeric AS PartNumberNumeric,
                    i.name_en AS NameEn,
                    i.name_ar AS NameAr,
                    i.name_ar_colloquial AS NameArColloquial,
                    i.brand AS Brand,
                    i.category_path::text AS CategoryPath,
                    i.has_warranty AS HasWarranty,
                    i.warranty_months AS WarrantyMonths,
                    i.is_batch_tracked AS IsBatchTracked,
                    i.reorder_level AS ReorderLevel,
                    i.is_active AS IsActive,
                    i.is_stop_ship AS IsStopShip,
                    i.stop_ship_reason AS StopShipReason,
                    i.notes AS Notes
                FROM items i
                INNER JOIN skus s ON s.id = i.sku_id
                WHERE s.barcode = @Barcode
                LIMIT 1;
                """,
                new { Barcode = request.Barcode.Trim() },
                cancellationToken: cancellationToken));

        return dto is null
            ? Result<ItemDto>.Failure(new Error("Item.NotFound", "Item was not found."))
            : Result<ItemDto>.Success(dto);
    }
}

public sealed record CreateItemCommand(CreateItemRequest Request, string IdempotencyKey)
    : IRequest<Result<ItemDto>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Items.Create;
    public string AuditModule => "ITEMS";
}

public sealed class CreateItemCommandValidator : AbstractValidator<CreateItemCommand>
{
    public CreateItemCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Request.PartNumber).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Request.NameEn).NotEmpty().Length(2, 300);
        RuleFor(x => x.Request.NameAr).NotEmpty().Length(2, 300);
        RuleFor(x => x.Request.ReorderLevel).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.WarrantyMonths).GreaterThan(0).When(x => x.Request.HasWarranty);
    }
}

public sealed class CreateItemCommandHandler : IRequestHandler<CreateItemCommand, Result<ItemDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IPartNumberService _partNumberService;

    public CreateItemCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, IPartNumberService partNumberService)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _partNumberService = partNumberService;
    }

    public async Task<Result<ItemDto>> Handle(CreateItemCommand request, CancellationToken cancellationToken)
    {
        var createResult = Item.Create(
            request.Request.SkuId,
            request.Request.PartNumber,
            request.Request.NameEn,
            request.Request.NameAr,
            request.Request.NameArColloquial,
            request.Request.Brand,
            request.Request.CategoryPath,
            request.Request.HasWarranty,
            request.Request.WarrantyMonths,
            request.Request.IsBatchTracked,
            request.Request.ReorderLevel,
            _partNumberService,
            _currentUser.UserId);

        if (createResult.IsFailure || createResult.Value is null)
        {
            return Result<ItemDto>.Failure(createResult.Error);
        }

        var entity = createResult.Value;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var dto = await connection.QuerySingleAsync<ItemDto>(
            new CommandDefinition(
                """
                INSERT INTO items (
                    id, sku_id, part_number, name_en, name_ar, name_ar_colloquial,
                    brand, category_path, unit_of_measure, has_warranty, warranty_months,
                    is_batch_tracked, reorder_level, is_active, is_stop_ship, stop_ship_reason,
                    notes, created_at, created_by, updated_at, updated_by)
                VALUES (
                    @Id, @SkuId, @PartNumber, @NameEn, @NameAr, @NameArColloquial,
                    @Brand, CAST(@CategoryPath AS ltree), 'PIECE', @HasWarranty, @WarrantyMonths,
                    @IsBatchTracked, @ReorderLevel, TRUE, FALSE, NULL,
                    @Notes, now(), @CreatedBy, NULL, NULL)
                RETURNING
                    id AS Id,
                    sku_id AS SkuId,
                    part_number AS PartNumber,
                    part_number_canonical AS PartNumberCanonical,
                    part_number_numeric AS PartNumberNumeric,
                    name_en AS NameEn,
                    name_ar AS NameAr,
                    name_ar_colloquial AS NameArColloquial,
                    brand AS Brand,
                    category_path::text AS CategoryPath,
                    has_warranty AS HasWarranty,
                    warranty_months AS WarrantyMonths,
                    is_batch_tracked AS IsBatchTracked,
                    reorder_level AS ReorderLevel,
                    is_active AS IsActive,
                    is_stop_ship AS IsStopShip,
                    stop_ship_reason AS StopShipReason,
                    notes AS Notes;
                """,
                new
                {
                    entity.Id,
                    entity.SkuId,
                    entity.PartNumber,
                    entity.NameEn,
                    entity.NameAr,
                    entity.NameArColloquial,
                    entity.Brand,
                    entity.CategoryPath,
                    entity.HasWarranty,
                    entity.WarrantyMonths,
                    entity.IsBatchTracked,
                    entity.ReorderLevel,
                    Notes = request.Request.Notes,
                    CreatedBy = _currentUser.UserId
                },
                cancellationToken: cancellationToken));

        return Result<ItemDto>.Success(dto);
    }
}

public sealed record UpdateItemCommand(Guid ItemId, UpdateItemRequest Request)
    : IRequest<Result<ItemDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Items.Update;
    public string AuditModule => "ITEMS";
}

public sealed class UpdateItemCommandValidator : AbstractValidator<UpdateItemCommand>
{
    public UpdateItemCommandValidator()
    {
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.Request.NameEn).NotEmpty().Length(2, 300);
        RuleFor(x => x.Request.NameAr).NotEmpty().Length(2, 300);
        RuleFor(x => x.Request.ReorderLevel).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateItemCommandHandler : IRequestHandler<UpdateItemCommand, Result<ItemDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public UpdateItemCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<ItemDto>> Handle(UpdateItemCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var dto = await connection.QuerySingleOrDefaultAsync<ItemDto>(
            new CommandDefinition(
                """
                UPDATE items
                SET
                    name_en = @NameEn,
                    name_ar = @NameAr,
                    name_ar_colloquial = @NameArColloquial,
                    brand = @Brand,
                    category_path = CAST(@CategoryPath AS ltree),
                    has_warranty = @HasWarranty,
                    warranty_months = @WarrantyMonths,
                    is_batch_tracked = @IsBatchTracked,
                    reorder_level = @ReorderLevel,
                    is_active = @IsActive,
                    notes = @Notes,
                    updated_at = now(),
                    updated_by = @UpdatedBy
                WHERE id = @Id
                RETURNING
                    id AS Id,
                    sku_id AS SkuId,
                    part_number AS PartNumber,
                    part_number_canonical AS PartNumberCanonical,
                    part_number_numeric AS PartNumberNumeric,
                    name_en AS NameEn,
                    name_ar AS NameAr,
                    name_ar_colloquial AS NameArColloquial,
                    brand AS Brand,
                    category_path::text AS CategoryPath,
                    has_warranty AS HasWarranty,
                    warranty_months AS WarrantyMonths,
                    is_batch_tracked AS IsBatchTracked,
                    reorder_level AS ReorderLevel,
                    is_active AS IsActive,
                    is_stop_ship AS IsStopShip,
                    stop_ship_reason AS StopShipReason,
                    notes AS Notes;
                """,
                new
                {
                    Id = request.ItemId,
                    request.Request.NameEn,
                    request.Request.NameAr,
                    request.Request.NameArColloquial,
                    request.Request.Brand,
                    request.Request.CategoryPath,
                    request.Request.HasWarranty,
                    request.Request.WarrantyMonths,
                    request.Request.IsBatchTracked,
                    request.Request.ReorderLevel,
                    request.Request.IsActive,
                    request.Request.Notes,
                    UpdatedBy = _currentUser.UserId
                },
                cancellationToken: cancellationToken));

        return dto is null
            ? Result<ItemDto>.Failure(new Error("Item.NotFound", "Item was not found."))
            : Result<ItemDto>.Success(dto);
    }
}

public sealed record AddItemAliasCommand(Guid ItemId, AddItemAliasRequest Request)
    : IRequest<Result<ItemAliasDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Items.ManageAliases;
    public string AuditModule => "ITEMS";
}

public sealed class AddItemAliasCommandValidator : AbstractValidator<AddItemAliasCommand>
{
    public AddItemAliasCommandValidator()
    {
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.Request.Alias).NotEmpty().MaximumLength(120);
    }
}

public sealed class AddItemAliasCommandHandler : IRequestHandler<AddItemAliasCommand, Result<ItemAliasDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPartNumberService _partNumberService;

    public AddItemAliasCommandHandler(IDbConnectionFactory connectionFactory, IPartNumberService partNumberService)
    {
        _connectionFactory = connectionFactory;
        _partNumberService = partNumberService;
    }

    public async Task<Result<ItemAliasDto>> Handle(AddItemAliasCommand request, CancellationToken cancellationToken)
    {
        var normalized = _partNumberService.NormalizePartNumber(request.Request.Alias);
        if (string.IsNullOrWhiteSpace(normalized.Canonical))
        {
            return Result<ItemAliasDto>.Failure(new Error("Item.AliasInvalid", "Alias is invalid."));
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var dto = await connection.QuerySingleOrDefaultAsync<ItemAliasDto>(
            new CommandDefinition(
                """
                INSERT INTO item_aliases (id, item_id, alias, source, created_at)
                VALUES (@Id, @ItemId, @Alias, @Source, now())
                ON CONFLICT (item_id, alias_canonical) DO NOTHING
                RETURNING
                    id AS Id,
                    item_id AS ItemId,
                    alias AS Alias,
                    alias_canonical AS AliasCanonical,
                    source AS Source,
                    created_at AS CreatedAt;
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    request.ItemId,
                    Alias = request.Request.Alias.Trim(),
                    Source = string.IsNullOrWhiteSpace(request.Request.Source) ? "MANUAL" : request.Request.Source.Trim().ToUpperInvariant()
                },
                cancellationToken: cancellationToken));

        if (dto is null)
        {
            return Result<ItemAliasDto>.Failure(new Error("Item.AliasDuplicate", "Alias already exists for this item."));
        }

        return Result<ItemAliasDto>.Success(dto);
    }
}

public sealed record AddItemInterchangeCommand(Guid ItemId, AddItemInterchangeRequest Request)
    : IRequest<Result<ItemInterchangeDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Items.ManageInterchanges;
    public string AuditModule => "ITEMS";
}

public sealed class AddItemInterchangeCommandValidator : AbstractValidator<AddItemInterchangeCommand>
{
    public AddItemInterchangeCommandValidator()
    {
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.Request.InterchangeItemId).NotEmpty();
        RuleFor(x => x.Request.Type).NotEmpty().MaximumLength(32);
    }
}

public sealed class AddItemInterchangeCommandHandler : IRequestHandler<AddItemInterchangeCommand, Result<ItemInterchangeDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public AddItemInterchangeCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<ItemInterchangeDto>> Handle(AddItemInterchangeCommand request, CancellationToken cancellationToken)
    {
        if (request.ItemId == request.Request.InterchangeItemId)
        {
            return Result<ItemInterchangeDto>.Failure(new Error("Item.InterchangeSelf", "An item cannot be interchangeable with itself."));
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var dto = await connection.QuerySingleOrDefaultAsync<ItemInterchangeDto>(
            new CommandDefinition(
                """
                INSERT INTO item_interchanges (
                    id, item_id, interchange_item_id, type, priority, notes, is_active, created_at, created_by)
                VALUES (
                    @Id, @ItemId, @InterchangeItemId, @Type, @Priority, @Notes, TRUE, now(), @CreatedBy)
                ON CONFLICT (item_id, interchange_item_id) DO NOTHING
                RETURNING
                    i.id AS Id,
                    i.item_id AS ItemId,
                    i.interchange_item_id AS InterchangeItemId,
                    ii.part_number_canonical AS InterchangePartNumber,
                    ii.name_ar AS InterchangeNameAr,
                    i.type AS Type,
                    i.priority AS Priority,
                    i.is_active AS IsActive
                FROM item_interchanges i
                INNER JOIN items ii ON ii.id = i.interchange_item_id
                WHERE i.id = @Id;
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    request.ItemId,
                    InterchangeItemId = request.Request.InterchangeItemId,
                    Type = request.Request.Type.Trim().ToUpperInvariant(),
                    Priority = request.Request.Priority <= 0 ? 1 : request.Request.Priority,
                    request.Request.Notes,
                    CreatedBy = _currentUser.UserId
                },
                cancellationToken: cancellationToken));

        return dto is null
            ? Result<ItemInterchangeDto>.Failure(new Error("Item.InterchangeDuplicate", "Interchange already exists for this item."))
            : Result<ItemInterchangeDto>.Success(dto);
    }
}

public sealed record MarkItemStopShipCommand(Guid ItemId, MarkItemStopShipRequest Request)
    : IRequest<Result>, IAuthorizedRequest, IAuditableRequest, IMakerCheckerRequest
{
    public string RequiredPermission => PermissionCodes.Items.StopShip;
    public string AuditModule => "ITEMS";
    public bool RequiresApproval => true;
}

public sealed class MarkItemStopShipCommandValidator : AbstractValidator<MarkItemStopShipCommand>
{
    public MarkItemStopShipCommandValidator()
    {
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.Request.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class MarkItemStopShipCommandHandler : IRequestHandler<MarkItemStopShipCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public MarkItemStopShipCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(MarkItemStopShipCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE items
                SET is_stop_ship = TRUE,
                    stop_ship_reason = @Reason,
                    updated_at = now(),
                    updated_by = @UpdatedBy
                WHERE id = @Id;
                """,
                new
                {
                    Id = request.ItemId,
                    Reason = request.Request.Reason.Trim(),
                    UpdatedBy = _currentUser.UserId
                },
                cancellationToken: cancellationToken));

        return affected == 0
            ? Result.Failure(new Error("Item.NotFound", "Item was not found."))
            : Result.Success();
    }
}

public sealed record GetItemInterchangesQuery(Guid ItemId) : IRequest<Result<IReadOnlyCollection<ItemInterchangeDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Items.Read;
}

public sealed class GetItemInterchangesQueryHandler : IRequestHandler<GetItemInterchangesQuery, Result<IReadOnlyCollection<ItemInterchangeDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetItemInterchangesQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<IReadOnlyCollection<ItemInterchangeDto>>> Handle(GetItemInterchangesQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ItemInterchangeDto>(
            new CommandDefinition(
                """
                SELECT
                    i.id AS Id,
                    i.item_id AS ItemId,
                    i.interchange_item_id AS InterchangeItemId,
                    ii.part_number_canonical AS InterchangePartNumber,
                    ii.name_ar AS InterchangeNameAr,
                    i.type AS Type,
                    i.priority AS Priority,
                    i.is_active AS IsActive
                FROM item_interchanges i
                INNER JOIN items ii ON ii.id = i.interchange_item_id
                WHERE i.item_id = @ItemId
                ORDER BY i.priority ASC, ii.part_number_canonical ASC;
                """,
                new { request.ItemId },
                cancellationToken: cancellationToken));

        return Result<IReadOnlyCollection<ItemInterchangeDto>>.Success(rows.ToArray());
    }
}
