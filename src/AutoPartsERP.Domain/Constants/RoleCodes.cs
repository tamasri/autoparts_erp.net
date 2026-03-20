namespace AutoPartsERP.Domain.Constants;

public static class RoleCodes
{
    public const string SystemAdministrator = "SYSTEM_ADMIN";
    public const string SecurityAdministrator = "SECURITY_ADMIN";
    public const string ComplianceOfficer = "COMPLIANCE_OFFICER";
    public const string Approver = "APPROVER";
    public const string Auditor = "AUDITOR";
    public const string StandardUser = "STANDARD_USER";

    public static readonly IReadOnlyCollection<string> All = new[]
    {
        SystemAdministrator,
        SecurityAdministrator,
        ComplianceOfficer,
        Approver,
        Auditor,
        StandardUser
    };
}
