namespace AutoPartsERP.Domain.Party;

public sealed class PartyContact : AuditableEntity
{
    public PartyContact(Guid partyId, string type, string value, string? label, bool isPrimary, Guid createdBy)
        : base(Guid.NewGuid())
    {
        PartyId = partyId;
        Type = type.Trim().ToUpperInvariant();
        Value = value.Trim();
        Label = label?.Trim();
        IsPrimary = isPrimary;
        CreatedBy = createdBy;
    }

    private PartyContact()
        : base(Guid.NewGuid())
    {
    }

    public Guid PartyId { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public string? Label { get; private set; }

    public bool IsPrimary { get; private set; }

    public Guid CreatedBy { get; private set; }
}
