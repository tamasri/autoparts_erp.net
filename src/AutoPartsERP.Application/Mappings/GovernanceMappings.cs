namespace AutoPartsERP.Application.Mappings;

public sealed class GovernanceMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<AppUser, UserSummaryDto>()
            .Map(destination => destination.Roles, _ => Array.Empty<UserRoleDto>());

        config.NewConfig<AppUser, UserDetailsDto>()
            .Map(destination => destination.Roles, _ => Array.Empty<UserRoleDto>())
            .Map(destination => destination.Permissions, _ => Array.Empty<string>());

        config.NewConfig<AppRole, RoleSummaryDto>()
            .Map(destination => destination.Permissions, source => source.Permissions.ToArray());

        config.NewConfig<ApprovalDecision, ApprovalDecisionDto>();
        config.NewConfig<ApprovalRequest, ApprovalRequestDto>();
        config.NewConfig<PeriodLock, PeriodLockDto>();
        config.NewConfig<ReasonCode, ReasonCodeDto>();

        config.NewConfig<Customer, CustomerDto>()
            .Map(destination => destination.Type, source => source.Type.ToString())
            .Map(destination => destination.StatusDisplay, source => source.IsActive ? "Active" : "Inactive")
            .Map(destination => destination.CreatedAt, source => source.CreatedAtUtc)
            .Map(destination => destination.UpdatedAt, source => source.UpdatedAtUtc);

        config.NewConfig<FxRate, FxRateDto>()
            .Map(destination => destination.CreatedAt, source => source.CreatedAtUtc);

        config.NewConfig<Category, CategoryDto>()
            .Map(destination => destination.Path, source => source.Path.Value);

        config.NewConfig<Sku, SkuDto>();

        config.NewConfig<Invoice, InvoiceListItemDto>()
            .Map(destination => destination.Type, source => source.Type.ToString())
            .Map(destination => destination.Status, source => source.Status.ToString())
            .Map(destination => destination.StatusDisplay, source => source.Status.ToString())
            .Map(destination => destination.TypeDisplay, source => source.Type.ToString());

        config.NewConfig<InvoiceLine, InvoiceLineDto>()
            .Map(destination => destination.LineTotalSyp, source => source.LineTotalSyp)
            .Map(destination => destination.LineTotalUsd, source => source.LineTotalUsd)
            .Map(destination => destination.GrossMarginSyp, source => source.GrossMarginSyp)
            .Map(destination => destination.GrossMarginUsd, source => source.GrossMarginUsd)
            .Map(destination => destination.GrossMarginPct, source => source.GrossMarginPct);

        config.NewConfig<Invoice, InvoiceDto>()
            .Map(destination => destination.Type, source => source.Type.ToString())
            .Map(destination => destination.Status, source => source.Status.ToString())
            .Map(destination => destination.StatusDisplay, source => source.Status.ToString())
            .Map(destination => destination.TypeDisplay, source => source.Type.ToString())
            .Map(destination => destination.Lines, source => source.Lines.Adapt<IReadOnlyCollection<InvoiceLineDto>>());

        config.NewConfig<Payment, PaymentDto>()
            .Map(destination => destination.PaymentType, source => source.PaymentType.ToString())
            .Map(destination => destination.PaymentMethod, source => source.PaymentMethod.ToString())
            .Map(destination => destination.UnallocatedSyp, source => source.UnallocatedSyp)
            .Map(destination => destination.UnallocatedUsd, source => source.UnallocatedUsd);

        config.NewConfig<WarrantyRecord, WarrantyRecordDto>()
            .Map(destination => destination.Status, source => source.Status.ToString())
            .Map(destination => destination.StatusDisplay, source => source.Status.ToString());
    }
}
