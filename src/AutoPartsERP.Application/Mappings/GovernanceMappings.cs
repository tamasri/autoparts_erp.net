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
    }
}
