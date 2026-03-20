namespace AutoPartsERP.Domain.Common;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public bool IsNone => string.IsNullOrWhiteSpace(Code);
}
