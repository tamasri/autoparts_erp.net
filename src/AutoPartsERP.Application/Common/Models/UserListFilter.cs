namespace AutoPartsERP.Application.Common.Models;

public sealed record UserListFilter(int PageNumber = 1, int PageSize = 50, bool? IsActive = null, string? RoleCode = null, string? Search = null);
