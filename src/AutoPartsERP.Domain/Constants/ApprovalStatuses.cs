namespace AutoPartsERP.Domain.Constants;

public static class ApprovalStatuses
{
    public const string Pending = "PENDING";
    public const string InReview = "IN_REVIEW";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string Cancelled = "CANCELLED";

    public static readonly IReadOnlyCollection<string> TerminalStatuses = new[]
    {
        Approved,
        Rejected,
        Cancelled
    };
}
