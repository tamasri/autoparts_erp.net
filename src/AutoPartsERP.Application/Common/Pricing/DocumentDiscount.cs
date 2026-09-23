namespace AutoPartsERP.Application.Common.Pricing;

/// <summary>
/// The discount on a whole sales or purchase invoice, on top of each line's own discount. It is given either as a percentage of the lines'
/// total (and then follows the lines when they change) or as a fixed amount; never both. Amounts are rounded to the 4 decimals the database keeps.
/// </summary>
public static class DocumentDiscount
{
    public sealed record Resolved(decimal? Percent, decimal Amount);

    public static Result<Resolved> Resolve(decimal subtotal, decimal? percent, decimal? amount)
    {
        if (percent is not null && amount is not null)
        {
            return Result<Resolved>.Failure(new Error("Validation.Discount", "Give the invoice discount as a percentage or as an amount, not both."));
        }

        if (percent is { } p)
        {
            return p is < 0 or > 100
                ? Result<Resolved>.Failure(new Error("Validation.Discount", "The discount percentage must be between 0 and 100."))
                : Result<Resolved>.Success(new Resolved(p == 0 ? null : p, Math.Round(subtotal * p / 100m, 4)));
        }

        if (amount is { } a)
        {
            if (a < 0)
            {
                return Result<Resolved>.Failure(new Error("Validation.Discount", "The discount amount cannot be negative."));
            }

            return a > subtotal
                ? Result<Resolved>.Failure(new Error("Validation.Discount", "The discount is larger than the invoice lines."))
                : Result<Resolved>.Success(new Resolved(null, Math.Round(a, 4)));
        }

        return Result<Resolved>.Success(new Resolved(null, 0));
    }

    /// <summary>What remains of each currency unit of the lines after the invoice discount; the net cost of a purchased item is scaled by it.</summary>
    public static decimal NetFactor(decimal subtotal, decimal discountAmount) => subtotal <= 0 ? 1 : (subtotal - discountAmount) / subtotal;
}
