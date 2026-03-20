namespace AutoPartsERP.Domain.Constants;

public static class PartyTypeCodes
{
    public const string Customer = "CUSTOMER";
    public const string Vendor = "VENDOR";
    public const string Employee = "EMPLOYEE";
    public const string DeliveryCompany = "DELIVERY_COMPANY";
    public const string Government = "GOVERNMENT";

    public static readonly IReadOnlyCollection<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Customer,
        Vendor,
        Employee,
        DeliveryCompany,
        Government
    };
}
