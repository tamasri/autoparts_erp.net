namespace AutoPartsERP.Contracts.Customers;

public sealed record CreateCustomerRequest(
    string Code,
    string Name,
    string Type,
    string? Phone,
    string? Phone2,
    string? Address,
    string? City,
    decimal CreditLimitSyp,
    decimal CreditLimitUsd,
    int PaymentTermsDays,
    Guid? AssignedSalesRep,
    string? Notes);

public sealed record UpdateCustomerRequest(
    string Name,
    string Type,
    string? Phone,
    string? Phone2,
    string? Address,
    string? City,
    decimal CreditLimitSyp,
    decimal CreditLimitUsd,
    int PaymentTermsDays,
    Guid? AssignedSalesRep,
    string? Notes);

public sealed record CustomerQueryRequest(
    int PageNumber = 1,
    int PageSize = 20,
    string? Type = null,
    bool? IsActive = null,
    string? SearchTerm = null,
    Guid? AssignedSalesRep = null);
