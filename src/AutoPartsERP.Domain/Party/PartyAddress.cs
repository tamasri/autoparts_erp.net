namespace AutoPartsERP.Domain.Party;

public sealed class PartyAddress : AuditableEntity
{
    public PartyAddress(
        Guid partyId,
        string type,
        string line1,
        string? line2,
        string? city,
        string? region,
        string country,
        bool isDefault)
        : base(Guid.NewGuid())
    {
        PartyId = partyId;
        Type = type.Trim().ToUpperInvariant();
        Line1 = line1.Trim();
        Line2 = line2?.Trim();
        City = city?.Trim();
        Region = region?.Trim();
        Country = string.IsNullOrWhiteSpace(country) ? "SY" : country.Trim().ToUpperInvariant();
        IsDefault = isDefault;
    }

    private PartyAddress()
        : base(Guid.NewGuid())
    {
    }

    public Guid PartyId { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string Line1 { get; private set; } = string.Empty;

    public string? Line2 { get; private set; }

    public string? City { get; private set; }

    public string? Region { get; private set; }

    public string Country { get; private set; } = "SY";

    public bool IsDefault { get; private set; }
}
