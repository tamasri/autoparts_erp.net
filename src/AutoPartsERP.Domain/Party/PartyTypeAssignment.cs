namespace AutoPartsERP.Domain.Party;

public sealed class PartyTypeAssignment : AuditableEntity
{
    private PartyTypeAssignment(
        Guid id,
        Guid partyId,
        string typeCode,
        bool isActive,
        Guid requestedBy,
        Guid? approvalId)
        : base(id)
    {
        PartyId = partyId;
        TypeCode = typeCode;
        IsActive = isActive;
        RequestedBy = requestedBy;
        ApprovalId = approvalId;
    }

    private PartyTypeAssignment()
        : base(Guid.NewGuid())
    {
    }

    public Guid PartyId { get; private set; }

    public string TypeCode { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public Guid RequestedBy { get; private set; }

    public Guid? ApprovedBy { get; private set; }

    public Guid? ApprovalId { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public DateTimeOffset? DeactivatedAt { get; private set; }

    public static PartyTypeAssignment CreatePending(Guid partyId, string typeCode, Guid requestedBy, Guid? approvalId)
    {
        return new PartyTypeAssignment(
            Guid.NewGuid(),
            partyId,
            typeCode.Trim().ToUpperInvariant(),
            false,
            requestedBy,
            approvalId);
    }

    public void Activate(Guid approvedBy)
    {
        IsActive = true;
        ApprovedBy = approvedBy;
        ActivatedAt = DateTimeOffset.UtcNow;
        DeactivatedAt = null;
        Touch();
    }

    public void Deactivate(Guid deactivatedBy)
    {
        IsActive = false;
        ApprovedBy = deactivatedBy;
        DeactivatedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
