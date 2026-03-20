namespace AutoPartsERP.Contracts.Common;

public sealed record PagedRequest(int PageNumber = 1, int PageSize = 50);
