namespace AutoPartsERP.Application.Common.Models;

public sealed record ApprovalListFilter(int PageNumber = 1, int PageSize = 50, bool ExcludeCurrentUserRequests = false, Guid? CurrentUserId = null);
