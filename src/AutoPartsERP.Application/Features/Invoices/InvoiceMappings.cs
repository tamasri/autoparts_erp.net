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

    public static InvoiceDto ToInvoiceDto(
        Guid id,
        string invoiceNumber,
        string status,
        string type,
        Guid customerId,
        string customerCode,
        string customerName,
        DateOnly invoiceDate,
        DateOnly dueDate,
        decimal totalSyp,
        decimal totalUsd,
        decimal paidSyp,
        decimal paidUsd,
        IReadOnlyCollection<InvoiceLineDto> lines)
    {
        return new InvoiceDto(
            id,
            invoiceNumber,
            status,
            type,
            customerId,
            customerCode,
            customerName,
            invoiceDate,
            dueDate,
            totalSyp,
            totalUsd,
            paidSyp,
            paidUsd,
            totalSyp - paidSyp,
            totalUsd - paidUsd,
            status.Humanize(LetterCasing.Title),
            type.Humanize(LetterCasing.Title),
            GetDueDateDisplay(dueDate),
            ToArabicWords(totalSyp, "ليرة سورية"),
            ToArabicWords(totalUsd, "دولار أمريكي"),
            lines);
    }

    public static string GetDueDateDisplay(DateOnly dueDate)
    {
        var due = dueDate.ToDateTime(TimeOnly.MinValue);
        return dueDate < DateOnly.FromDateTime(DateTime.UtcNow)
            ? $"متأخر {(DateTime.UtcNow - due).Humanize(culture: Arabic)}"
            : due.Humanize(culture: Arabic);
    }

    public static string GetTimeAgo(DateTimeOffset value) => value.Humanize(culture: Arabic);
}
