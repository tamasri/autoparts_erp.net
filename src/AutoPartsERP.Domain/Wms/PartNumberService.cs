using System.Text.RegularExpressions;

namespace AutoPartsERP.Domain.Wms;

public sealed record PartNumberNormalizationResult(string Canonical, string Numeric);

public interface IPartNumberService
{
    PartNumberNormalizationResult NormalizePartNumber(string? raw);
    string ToNumericOnly(string? raw);
    bool IsMatch(string? left, string? right);
}

public sealed class PartNumberService : IPartNumberService
{
    private static readonly Regex CanonicalPattern = new("[^A-Za-z0-9]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NumericPattern = new("[^0-9]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public PartNumberNormalizationResult NormalizePartNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new PartNumberNormalizationResult(string.Empty, string.Empty);
        }

        var canonical = CanonicalPattern.Replace(raw.Trim(), string.Empty).ToUpperInvariant();
        var numeric = NumericPattern.Replace(raw.Trim(), string.Empty);
        return new PartNumberNormalizationResult(canonical, numeric);
    }

    public string ToNumericOnly(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return NumericPattern.Replace(raw.Trim(), string.Empty);
    }

    public bool IsMatch(string? left, string? right)
    {
        var leftValue = NormalizePartNumber(left);
        var rightValue = NormalizePartNumber(right);

        if (!string.IsNullOrWhiteSpace(leftValue.Canonical) &&
            string.Equals(leftValue.Canonical, rightValue.Canonical, StringComparison.Ordinal))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(leftValue.Numeric) &&
               string.Equals(leftValue.Numeric, rightValue.Numeric, StringComparison.Ordinal);
    }
}
