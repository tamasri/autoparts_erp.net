using AutoPartsERP.Domain.Operational.Enums;

namespace AutoPartsERP.Domain.Operational;

public sealed class Customer : AuditableEntity
{
    private Customer(
        Guid id,
        string code,
        string name,
        CustomerType type,
        decimal creditLimitSyp,
        decimal creditLimitUsd,
        int paymentTermsDays,
        Guid createdBy)
        : base(id)
    {
        Code = code;
        Name = name;
        Type = type;
        CreditLimitSyp = creditLimitSyp;
        CreditLimitUsd = creditLimitUsd;
        PaymentTermsDays = paymentTermsDays;
        IsActive = true;
        CreatedBy = createdBy;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public CustomerType Type { get; private set; }

    public string? Phone { get; private set; }

    public string? Phone2 { get; private set; }

    public string? Address { get; private set; }

    public string? City { get; private set; }

    public string? Region { get; private set; }

    public string? TaxNumber { get; private set; }

    public decimal CreditLimitSyp { get; private set; }

    public decimal CreditLimitUsd { get; private set; }

    public int PaymentTermsDays { get; private set; }

    public bool IsActive { get; private set; }

    public Guid? AssignedSalesRep { get; private set; }

    public string? Notes { get; private set; }

    public string? DeactivationReason { get; private set; }

    public Guid CreatedBy { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static Result<Customer> Create(
        string code,
        string name,
        CustomerType type,
        decimal creditLimitSyp,
        decimal creditLimitUsd,
        int paymentTermsDays,
        Guid createdBy)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result<Customer>.Failure(new Error("Customer.CodeRequired", "Customer code is required."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Customer>.Failure(new Error("Customer.NameRequired", "Customer name is required."));
        }

        if (creditLimitSyp < 0 || creditLimitUsd < 0)
        {
            return Result<Customer>.Failure(new Error("Customer.CreditLimitInvalid", "Credit limits must be non-negative."));
        }

        if (paymentTermsDays < 0)
        {
            return Result<Customer>.Failure(new Error("Customer.PaymentTermsInvalid", "Payment terms must be zero or greater."));
        }

        return Result<Customer>.Success(new Customer(
            Guid.NewGuid(),
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            type,
            creditLimitSyp,
            creditLimitUsd,
            paymentTermsDays,
            createdBy));
    }

    public Result CheckCreditLimit(decimal newSyp, decimal balanceSyp, decimal newUsd, decimal balanceUsd)
    {
        // 0 means unlimited credit in this model.
        if (CreditLimitSyp <= 0 && CreditLimitUsd <= 0)
        {
            return Result.Success();
        }

        if (CreditLimitSyp > 0 && (balanceSyp + newSyp) > CreditLimitSyp)
        {
            return Result.Failure(new Error("Customer.CreditLimitExceeded", "SYP credit limit exceeded."));
        }

        if (CreditLimitUsd > 0 && (balanceUsd + newUsd) > CreditLimitUsd)
        {
            return Result.Failure(new Error("Customer.CreditLimitExceeded", "USD credit limit exceeded."));
        }

        return Result.Success();
    }

    public Result Deactivate(string reason, Guid by)
    {
        if (!IsActive)
        {
            return Result.Success();
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(new Error("Customer.DeactivateReasonRequired", "A deactivation reason is required."));
        }

        IsActive = false;
        DeactivationReason = reason.Trim();
        UpdatedBy = by;
        Touch();
        return Result.Success();
    }

    public void AssignSalesRep(Guid salesRepId)
    {
        AssignedSalesRep = salesRepId == Guid.Empty ? null : salesRepId;
        Touch();
    }

    public void UpdateDetails(
        string name,
        CustomerType type,
        string? phone,
        string? phone2,
        string? address,
        string? city,
        string? region,
        string? taxNumber,
        decimal creditLimitSyp,
        decimal creditLimitUsd,
        int paymentTermsDays,
        string? notes,
        Guid by)
    {
        Name = name.Trim();
        Type = type;
        Phone = phone?.Trim();
        Phone2 = phone2?.Trim();
        Address = address?.Trim();
        City = city?.Trim();
        Region = region?.Trim();
        TaxNumber = taxNumber?.Trim();
        CreditLimitSyp = creditLimitSyp;
        CreditLimitUsd = creditLimitUsd;
        PaymentTermsDays = paymentTermsDays;
        Notes = notes?.Trim();
        UpdatedBy = by;
        Touch();
    }
}
