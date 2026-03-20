namespace AutoPartsERP.Domain.Party;

public sealed class PartyNote : AuditableEntity
{
    public PartyNote(Guid partyId, string content, bool isPinned, Guid createdBy)
        : base(Guid.NewGuid())
    {
        PartyId = partyId;
        Content = content.Trim();
        IsPinned = isPinned;
        CreatedBy = createdBy;
    }

    private PartyNote()
        : base(Guid.NewGuid())
    {
    }

    public Guid PartyId { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public bool IsPinned { get; private set; }

    public Guid CreatedBy { get; private set; }
}
