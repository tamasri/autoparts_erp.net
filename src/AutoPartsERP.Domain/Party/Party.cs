using AutoPartsERP.Domain.Constants;

namespace AutoPartsERP.Domain.Party;

public sealed class Party : AuditableEntity
{
    private readonly List<PartyTypeAssignment> _typeAssignments = new();
    private readonly List<PartyContact> _contacts = new();
    private readonly List<PartyAddress> _addresses = new();
    private readonly List<PartyNote> _notes = new();

    private Party(Guid id, string code, string displayName, string displayNameAr, string? taxNumber, Guid createdBy)
        : base(id)
    {
        Code = code;
        DisplayName = displayName;
        DisplayNameAr = displayNameAr;
        TaxNumber = taxNumber;
        IsActive = true;
        CreatedBy = createdBy;
    }

    private Party()
        : base(Guid.NewGuid())
    {
    }

    public string Code { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string DisplayNameAr { get; private set; } = string.Empty;

    public string? TaxNumber { get; private set; }

    public string? Website { get; private set; }

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; }

    public Guid CreatedBy { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public IReadOnlyList<PartyTypeAssignment> TypeAssignments => _typeAssignments.AsReadOnly();

    public IReadOnlyList<PartyContact> Contacts => _contacts.AsReadOnly();

    public IReadOnlyList<PartyAddress> Addresses => _addresses.AsReadOnly();

    public IReadOnlyList<PartyNote> NotesEntries => _notes.AsReadOnly();

    public bool IsCustomer => _typeAssignments.Any(a => a.TypeCode == PartyTypeCodes.Customer && a.IsActive);

    public bool IsVendor => _typeAssignments.Any(a => a.TypeCode == PartyTypeCodes.Vendor && a.IsActive);

    public bool IsEmployee => _typeAssignments.Any(a => a.TypeCode == PartyTypeCodes.Employee && a.IsActive);

    public bool HasCombinedStatement => IsCustomer && IsVendor;

    public static Result<Party> Create(
        string code,
        string displayName,
        string displayNameAr,
        string? taxNumber,
        Guid createdBy)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result<Party>.Failure(new Error("Party.NameRequired", "Display name is required."));
        }

        if (string.IsNullOrWhiteSpace(displayNameAr))
        {
            return Result<Party>.Failure(new Error("Party.NameArRequired", "Arabic display name is required."));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Result<Party>.Failure(new Error("Party.CodeRequired", "Party code is required."));
        }

        var party = new Party(
            Guid.NewGuid(),
            code.Trim().ToUpperInvariant(),
            displayName.Trim(),
            displayNameAr.Trim(),
            taxNumber?.Trim(),
            createdBy);

        return Result<Party>.Success(party);
    }

    public Result<PartyTypeAssignment> RequestTypeAssignment(string typeCode, Guid requestedBy, Guid? approvalId)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
        {
            return Result<PartyTypeAssignment>.Failure(
                new Error("Party.TypeCodeRequired", "Type code is required."));
        }

        var normalized = typeCode.Trim().ToUpperInvariant();

        if (_typeAssignments.Any(a => a.TypeCode == normalized && a.IsActive))
        {
            return Result<PartyTypeAssignment>.Failure(
                new Error("Party.TypeAlreadyAssigned", $"Type {normalized} is already active for this party."));
        }

        var assignment = PartyTypeAssignment.CreatePending(Id, normalized, requestedBy, approvalId);
        _typeAssignments.Add(assignment);
        Touch();
        return Result<PartyTypeAssignment>.Success(assignment);
    }

    public Result<bool> ActivateTypeAssignment(string typeCode, Guid approvedBy)
    {
        var normalized = typeCode.Trim().ToUpperInvariant();
        var assignment = _typeAssignments.FirstOrDefault(a => a.TypeCode == normalized && !a.IsActive);
        if (assignment is null)
        {
            return Result<bool>.Failure(
                new Error("Party.AssignmentNotFound", $"No pending assignment found for type {normalized}."));
        }

        assignment.Activate(approvedBy);
        Touch();
        return Result<bool>.Success(true);
    }

    public Result<bool> DeactivateTypeAssignment(string typeCode, Guid deactivatedBy)
    {
        var normalized = typeCode.Trim().ToUpperInvariant();
        var assignment = _typeAssignments.FirstOrDefault(a => a.TypeCode == normalized && a.IsActive);
        if (assignment is null)
        {
            return Result<bool>.Failure(
                new Error("Party.AssignmentNotFound", $"No active assignment found for type {normalized}."));
        }

        assignment.Deactivate(deactivatedBy);
        Touch();
        return Result<bool>.Success(true);
    }

    public void UpdateProfile(string displayName, string displayNameAr, string? taxNumber, string? website, string? notes, Guid by)
    {
        DisplayName = displayName.Trim();
        DisplayNameAr = displayNameAr.Trim();
        TaxNumber = taxNumber?.Trim();
        Website = website?.Trim();
        Notes = notes?.Trim();
        UpdatedBy = by;
        Touch();
    }

    public void AddContact(string type, string value, string? label, bool isPrimary, Guid by)
    {
        _contacts.Add(new PartyContact(Id, type, value, label, isPrimary, by));
        Touch();
    }

    public void AddAddress(string type, string line1, string? line2, string? city, string? region, string country, bool isDefault)
    {
        _addresses.Add(new PartyAddress(Id, type, line1, line2, city, region, country, isDefault));
        Touch();
    }

    public void PinNote(string content, Guid by)
    {
        _notes.Add(new PartyNote(Id, content, true, by));
        Touch();
    }
}
