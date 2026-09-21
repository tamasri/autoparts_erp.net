using AutoPartsERP.Application.Common.Models;

namespace AutoPartsERP.Application.Common.Abstractions;

public interface IUserService
{
    Task<Result<PagedResponse<UserSummaryDto>>> GetUsersAsync(UserListFilter filter, CancellationToken cancellationToken = default);

    Task<Result<UserDetailsDto>> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result<UserDetailsDto>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result<UserDetailsDto>> UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result<UserDetailsDto>> SetUserLockAsync(Guid userId, SetUserLockRequest request, CancellationToken cancellationToken = default);

    Task<Result<UserDetailsDto>> AssignRolesAsync(Guid userId, AssignUserRolesRequest request, CancellationToken cancellationToken = default);

    Task<Result<UserDetailsDto>> DeactivateUserAsync(Guid userId, string reason, string? reasonCode, CancellationToken cancellationToken = default);

    /// <summary>Lets a deactivated or locked-out user sign in again.</summary>
    Task<Result<UserDetailsDto>> ActivateUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Sets a new password chosen by an administrator (the password policy still applies) and clears the failed-attempt lock.</summary>
    Task<Result<UserDetailsDto>> ResetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken = default);
}
