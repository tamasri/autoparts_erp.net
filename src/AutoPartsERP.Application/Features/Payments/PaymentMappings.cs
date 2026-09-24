using System.Globalization;
using Humanizer;

namespace AutoPartsERP.Application.Features.Payments;

internal static class PaymentMappings
{
    private static readonly CultureInfo Arabic = new("ar");

    /// <summary>The columns of a <see cref="PaymentDto"/>, from payments <c>p</c> and its customer <c>c</c> (add WHERE / ORDER BY).</summary>
    public const string Select = """
        SELECT p.id AS Id, p.payment_number AS PaymentNumber, p.payment_type AS PaymentType, p.customer_id AS CustomerId, c.name AS CustomerName,
               p.payment_date AS PaymentDate, p.payment_method AS PaymentMethod, p.amount_syp AS AmountSyp, p.amount_usd AS AmountUsd,
               p.allocated_syp AS AllocatedSyp, p.allocated_usd AS AllocatedUsd, p.unallocated_syp AS UnallocatedSyp, p.unallocated_usd AS UnallocatedUsd,
               p.is_reversed AS IsReversed, p.payment_method AS PaymentMethodDisplay, '' AS ReceivedDisplay
        FROM payments p
        INNER JOIN customers c ON c.id = p.customer_id
        """;

    public static PaymentDto WithDisplay(PaymentDto item) => item with
    {
        PaymentMethodDisplay = item.PaymentMethod.Humanize(LetterCasing.Title),
        ReceivedDisplay = GetReceivedDisplay(item.PaymentDate)
    };

    public static string GetReceivedDisplay(DateOnly paymentDate)
    {
        return paymentDate.ToDateTime(TimeOnly.MinValue).Humanize(culture: Arabic);
    }

    public static PaymentDto ToPaymentDto(
        Guid id,
        string paymentNumber,
        string paymentType,
        Guid customerId,
        string customerName,
        DateOnly paymentDate,
        string paymentMethod,
        decimal amountSyp,
        decimal amountUsd,
        decimal allocatedSyp,
        decimal allocatedUsd,
        bool isReversed)
    {
        return new PaymentDto(
            id,
            paymentNumber,
            paymentType,
            customerId,
            customerName,
            paymentDate,
            paymentMethod,
            amountSyp,
            amountUsd,
            allocatedSyp,
            allocatedUsd,
            amountSyp - allocatedSyp,
            amountUsd - allocatedUsd,
            isReversed,
            paymentMethod.Humanize(LetterCasing.Title),
            GetReceivedDisplay(paymentDate));
    }
}
