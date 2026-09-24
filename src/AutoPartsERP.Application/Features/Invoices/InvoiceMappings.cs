using System.Globalization;
using AutoPartsERP.Domain.Extensions;
using Humanizer;

namespace AutoPartsERP.Application.Features.Invoices;

internal static class InvoiceMappings
{
    private static readonly CultureInfo Arabic = new("ar");

    public static string ToArabicWords(decimal amount, string currencyName)
    {
        var whole = (long)Math.Floor(Math.Abs(amount));
        var fraction = (int)Math.Round((Math.Abs(amount) - whole) * 100);
        var sign = amount < 0 ? "سالب " : string.Empty;
        var wholeWords = whole.ToWords(Arabic);
        var fractionPart = fraction > 0 ? $" و{fraction.ToWords(Arabic)} قرش" : string.Empty;
        return $"{sign}{wholeWords} {currencyName}{fractionPart} فقط لا غير";
    }

    public static InvoiceDto ToInvoiceDto(InvoiceHeaderRow h, IReadOnlyCollection<InvoiceLineDto> lines, DocumentLinkDto? returnOf, IReadOnlyCollection<DocumentLinkDto> returns) =>
        new(
            h.Id,
            h.InvoiceNumber ?? string.Empty,
            h.Status,
            h.Type,
            h.CustomerId,
            h.CustomerCode,
            h.CustomerName,
            h.InvoiceDate,
            h.DueDate,
            h.TotalSyp,
            h.TotalUsd,
            h.PaidSyp,
            h.PaidUsd,
            h.BalanceSyp,
            h.BalanceUsd,
            h.Status.Humanize(LetterCasing.Title),
            h.Type.Humanize(LetterCasing.Title),
            GetDueDateDisplay(h.DueDate),
            ToArabicWords(h.TotalSyp, "ليرة سورية"),
            ToArabicWords(h.TotalUsd, "دولار أمريكي"),
            lines,
            new InvoiceAmountsDto(h.SubtotalSyp, h.SubtotalUsd, h.DiscountPct, h.DiscountAmountSyp, h.DiscountAmountUsd, h.DeliveryFeeSyp, h.DeliveryFeeUsd),
            h.CreditAppliedSyp,
            h.CreditAppliedUsd,
            returnOf,
            returns);

    public static string GetDueDateDisplay(DateOnly dueDate)
    {
        var due = dueDate.ToDateTime(TimeOnly.MinValue);
        return dueDate < DateOnly.FromDateTime(DateTime.UtcNow)
            ? $"متأخر {(DateTime.UtcNow - due).Humanize(culture: Arabic)}"
            : due.Humanize(culture: Arabic);
    }

    public static string GetTimeAgo(DateTimeOffset value) => value.Humanize(culture: Arabic);
}
