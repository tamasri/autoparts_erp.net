namespace AutoPartsERP.Infrastructure.Services;

public sealed class ErpNextOptions
{
    public const string SectionName = "Erpnext";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>Currency the invoice unit prices (USD in our schema) are expressed in; must match the ERPNext company currency.</summary>
    public string Currency { get; set; } = "USD";
}
