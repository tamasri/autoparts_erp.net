using System.Globalization;
using Humanizer;

namespace AutoPartsERP.Application.Features.Payments;

internal static class PaymentMappings
{
    private static readonly CultureInfo Arabic = new("ar");

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
