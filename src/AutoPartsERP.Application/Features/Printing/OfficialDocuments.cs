using AutoPartsERP.Application.Features.CompanyProfile;
using AutoPartsERP.Application.Features.Customers.GetCustomerAccountStatement;
using static AutoPartsERP.Application.Features.Printing.PrintFormat;

namespace AutoPartsERP.Application.Features.Printing;

// The official printed forms, composed on the server from the database (never from what a browser sends): the sales invoice
// (and its returns / credit notes), the receipt voucher, and the customer account statement. The builders are pure (data in,
// ExportDocument with a layout out); the handlers read the data. The renderer adds the company's details from the profile.
// Currency: a document prints in the currency its amounts were recorded in — an invoice in Syrian pounds (it holds both, at its
// rate) with the dollar equivalent beside the total; a receipt in the currency received; the account statement in dollars, the
// books' currency (receipts paid in dollars carry no pound amount, so only the dollar ledger is complete).

/// <summary>A customer as printed in a party block: name, then address, phone and e-mail lines.</summary>
public sealed record PrintedCustomer(string Code, string Name, string? Phone, string? Address, string? City, string? Email)
{
    public IReadOnlyList<string> Lines =>
        new[] { string.Join("، ", new[] { Address, City }.Where(s => !string.IsNullOrWhiteSpace(s))), Phone ?? string.Empty, Email ?? string.Empty }
            .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

    public static Task<PrintedCustomer?> LoadAsync(DbConnection connection, Guid customerId, CancellationToken ct) =>
        connection.QuerySingleOrDefaultAsync<PrintedCustomer>(new CommandDefinition(
            """
            SELECT c.code AS Code, c.name AS Name, c.phone AS Phone, c.address AS Address, c.city AS City,
                   (SELECT pc.value FROM party_contacts pc WHERE pc.party_id = c.party_id AND upper(pc.type) = 'EMAIL'
                    ORDER BY pc.is_primary DESC, pc.created_at LIMIT 1) AS Email
            FROM customers c WHERE c.id = @customerId;
            """,
            new { customerId }, cancellationToken: ct));
}

/// <summary>What a receipt voucher prints: the payment, who took it in, and the invoices it settled.</summary>
public sealed record PrintedReceipt(
    Guid Id, string PaymentNumber, string PaymentType, Guid CustomerId, DateOnly PaymentDate, string PaymentMethod, decimal AmountSyp,
    decimal AmountUsd, string? ChequeNumber, string? Notes, bool IsReversed, string? ReversalReason, string? ReceivedByName, string? Invoices);

// ------------------------------------------------------------------ sales invoice

public static class InvoicePrint
{
    public static ExportDocument Build(InvoiceDto invoice, PrintedCustomer? customer, CompanyProfileDto company, IAppLinks links)
    {
        var (title, subtitle) = invoice.Type switch
        {
            "RETURN" => ("مرتجع مبيعات", invoice.ReturnOf is { } of ? $"مرتجع من الفاتورة {of.Number}" : "إشعار استلام بضاعة مرتجعة"),
            "CREDIT_NOTE" => ("إشعار دائن", "إشعار تخفيض رصيد الزبون"),
            _ => ("فاتورة مبيعات", company.InvoiceSubtitle),
        };
        var sale = invoice.Type == "SALE";
        var inSyp = invoice.TotalSyp != 0 || invoice.Lines.Any(l => l.LineTotalSyp != 0);
        var unit = inSyp ? Syp : Usd;
        decimal Pick(decimal syp, decimal usd) => inSyp ? syp : usd;
        var number = string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? "مسودة" : invoice.InvoiceNumber;
        var lineDiscount = invoice.Lines.Any(l => l.DiscountPct != 0);

        var columns = new List<string> { "رقم المادة", "بيان المادة", "الكمية", "السعر" };
        if (lineDiscount) { columns.Add("الخصم %"); }
        columns.Add("الإجمالي");
        var rows = invoice.Lines.OrderBy(l => l.LineNumber).Select(l =>
        {
            var row = new List<string?> { l.SkuCode, l.SkuName, Raw(l.Quantity), Raw(Pick(l.UnitPriceSyp, l.UnitPriceUsd)) };
            if (lineDiscount) { row.Add(Raw(l.DiscountPct)); }
            row.Add(Raw(Math.Abs(Pick(l.LineTotalSyp, l.LineTotalUsd))));
            return (IReadOnlyList<string?>)row;
        }).ToList();
        int[] money = lineDiscount ? [3, 5] : [3, 4];
        int[] numeric = lineDiscount ? [2, 3, 4, 5] : [2, 3, 4];
        float[] widths = lineDiscount ? [1.5f, 3.2f, 1f, 1.4f, 1f, 1.5f] : [1.5f, 3.9f, 1.2f, 1.4f, 1.5f];

        var amounts = invoice.Amounts;
        var summary = new List<ExportField> { new("المجموع الفرعي", Amount(Math.Abs(Pick(amounts.SubtotalSyp, amounts.SubtotalUsd)), unit)) };
        var discount = Pick(amounts.DiscountAmountSyp, amounts.DiscountAmountUsd);
        if (discount != 0)
        {
            summary.Add(new ExportField(amounts.DiscountPct is { } pct ? $"خصم الفاتورة ({Raw(pct)}%)" : "خصم الفاتورة", Amount(discount, unit)));
        }

        var delivery = Pick(amounts.DeliveryFeeSyp, amounts.DeliveryFeeUsd);
        if (delivery != 0) { summary.Add(new ExportField("أجور التوصيل", Amount(delivery, unit))); }

        return new ExportDocument(
            title, subtitle, [],
            [new ExportTable(null, columns, rows, null, numeric, money, unit, [0], null, null, widths)],
            FileName: $"invoice-{number}",
            Layout: new ExportLayout(
                Meta:
                [
                    new ExportMeta(sale ? "رقم الفاتورة" : "رقم المستند", number),
                    new ExportMeta(sale ? "تاريخ إصدار الفاتورة" : "التاريخ", Date(invoice.InvoiceDate)),
                    new ExportMeta(sale ? "تاريخ استحقاق الدفع" : "الحالة", sale ? Date(invoice.DueDate) : invoice.StatusDisplay),
                ],
                Parties:
                [
                    new ExportParty(sale ? "فاتورة إلى" : "الزبون", customer?.Name ?? invoice.CustomerName, customer?.Lines ?? []),
                    new ExportParty("صادرة عن", Company: true),
                ],
                Summary: summary,
                Total: new ExportAmount("المجموع الكلي", Amount(Math.Abs(Pick(invoice.TotalSyp, invoice.TotalUsd)), unit), inSyp ? Amount(Math.Abs(invoice.TotalUsd), Usd) : null),
                Note: invoice.Status == "VOID" ? new ExportField("تنبيه", "هذه الفاتورة ملغاة ولا يُعتدّ بها") : null,
                Terms: sale ? CompanyProfiles.Terms(company) : null,
                RecipientBox: sale,
                PaymentDetails: true,
                Qr: new ExportQr(links.To($"/invoices/{invoice.Id}"))));
    }
}

// ------------------------------------------------------------------ receipt voucher

public static class ReceiptPrint
{
    private static readonly Dictionary<string, string> Methods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CASH"] = "نقداً", ["USD_CASH"] = "نقداً (دولار)", ["BANK_TRANSFER"] = "حوالة مصرفية", ["CHEQUE"] = "شيك",
    };

    public static ExportDocument Build(PrintedReceipt p, PrintedCustomer? customer, CompanyProfileDto company, IAppLinks links)
    {
        var refund = p.PaymentType == "REFUND";
        var inSyp = p.AmountSyp != 0;
        var amount = inSyp ? Amount(p.AmountSyp, Syp) : Amount(p.AmountUsd, Usd);
        var equivalent = inSyp && p.AmountUsd != 0 ? Amount(p.AmountUsd, Usd) : null;
        var words = inSyp ? SypInWords(p.AmountSyp) : UsdInWords(p.AmountUsd);
        var purpose = !string.IsNullOrWhiteSpace(p.Invoices) ? $"سداد قيمة الفاتورة رقم {p.Invoices}"
            : !string.IsNullOrWhiteSpace(p.Notes) ? p.Notes!
            : refund ? "ردّ مبلغ للزبون" : "دفعة على الحساب";
        if (!string.IsNullOrWhiteSpace(p.Invoices) && !string.IsNullOrWhiteSpace(p.Notes)) { purpose += $" - {p.Notes}"; }

        var method = Methods.GetValueOrDefault(p.PaymentMethod, p.PaymentMethod);
        if (!string.IsNullOrWhiteSpace(p.ChequeNumber)) { method += $" {p.ChequeNumber}"; }

        var note = p.IsReversed
            ? new ExportField("تنبيه", $"هذا السند معكوس ولا يُعتدّ به{(string.IsNullOrWhiteSpace(p.ReversalReason) ? string.Empty : $": {p.ReversalReason}")}")
            : string.IsNullOrWhiteSpace(company.ReceiptNote) ? null : new ExportField("ملاحظة", company.ReceiptNote);

        return new ExportDocument(
            refund ? "سند صرف" : "سند قبض", refund ? "سند صرف مالي رسمي معتمد" : "سند استلام مالي رسمي معتمد", [], [],
            FileName: $"receipt-{p.PaymentNumber}",
            Layout: new ExportLayout(
                Meta:
                [
                    new ExportMeta("رقم السند", p.PaymentNumber),
                    new ExportMeta("تاريخ التحرير", DayFirst(p.PaymentDate)),
                    new ExportMeta("طريقة الدفع", method, Badge: true),
                ],
                Parties:
                [
                    new ExportParty(refund ? "الجهة الدافعة" : "الجهة القابضة", Company: true, Accent: true, Icon: "building"),
                    new ExportParty(refund ? "بيانات المستلم" : "بيانات الدافع / المسلّم", customer?.Name, customer?.Lines ?? []),
                ],
                Receipt: new ExportReceipt(
                    new ExportAmount(refund ? "المبلغ المدفوع بالأرقام" : "المبلغ المقبوض بالأرقام", amount, equivalent),
                    [
                        new ExportField(refund ? "دفعنا إلى السيد / السادة" : "استلمنا من السيد / السادة", customer?.Name),
                        new ExportField("مبلغاً وقدره (كتابةً)", words),
                        new ExportField("وذلك لقاء / مقابل", purpose),
                    ],
                    WordsLine: 1),
                Note: note,
                Signatures:
                [
                    new ExportSignature("توقيع المستلم", p.ReceivedByName, p.ReceivedByName),
                    new ExportSignature("توقيع المحاسب"),
                    new ExportSignature("اعتماد الإدارة", company.ManagerName, company.ManagerName is null ? null : "المدير المسؤول"),
                ],
                Stamp: true,
                Qr: new ExportQr(links.To("/payments"), $"VERIFIED VOUCHER\n{p.PaymentNumber}")));
    }
}

// ------------------------------------------------------------------ customer account statement

public static class StatementPrint
{
    private static readonly Dictionary<string, string> Kinds = new(StringComparer.Ordinal)
    {
        ["INVOICE"] = "فاتورة مبيعات", ["VOIDED"] = "فاتورة مبيعات (ملغاة)", ["RETURN"] = "مرتجع مبيعات", ["CREDIT_NOTE"] = "إشعار دائن",
        ["PAYMENT"] = "سند قبض", ["REFUND"] = "ردّ مبلغ",
    };

    public static ExportDocument Build(
        CustomerAccountStatementDto statement, PrintedCustomer? customer, CompanyProfileDto company, IAppLinks links, string preparedBy, DateOnly today)
    {
        var lines = statement.Transactions.OrderBy(t => t.Date).ToList();
        var from = lines.Count > 0 ? DateOnly.FromDateTime(lines[0].Date) : today;
        static string Cell(decimal v) => v == 0 ? "-" : Raw(v);
        var rows = lines.Select(t => (IReadOnlyList<string?>)new List<string?>
        {
            DayFirst(DateOnly.FromDateTime(t.Date)), t.Reference, Kinds.GetValueOrDefault(t.Type, t.Type), Cell(t.DebitUsd), Cell(t.CreditUsd), Raw(t.BalanceUsd),
        }).ToList();

        var closing = lines.Count > 0 ? lines[^1].BalanceUsd : 0;
        var name = customer?.Name ?? statement.CustomerName;

        return new ExportDocument(
            "كشف حساب عميل", "بيان الحركات المالية والرصيد المستحق المعتمد", [],
            [new ExportTable(null, ["التاريخ", "رقم السند", "بيان الحركة / التفاصيل", "مدين (+)", "دائن (-)", "الرصيد"], rows,
                null, [3, 4, 5], [3, 4, 5], Usd, [0, 1], [4], 5, [1.45f, 1.75f, 2.4f, 1.35f, 1.35f, 1.45f])],
            FileName: $"statement-{statement.CustomerCode}",
            Layout: new ExportLayout(
                Meta:
                [
                    new ExportMeta("رقم الحساب", statement.CustomerCode),
                    new ExportMeta("تاريخ الكشف", DayFirst(today)),
                    new ExportMeta("الفترة", $"من {DayFirst(from)} إلى {DayFirst(today)}"),
                ],
                Parties:
                [
                    new ExportParty("كشف حساب السيد / السادة", name, customer?.Lines ?? [], Accent: true, Icon: "user"),
                    new ExportParty("الجهة المصدرة للكشف", Company: true),
                ],
                Opening: new ExportField("الرصيد الافتتاحي السابق (ما قبل الفترة)", Amount(0, Usd)),
                Cards:
                [
                    new ExportCard("إجمالي المدين (المبيعات)", Amount(lines.Sum(t => t.DebitUsd), Usd)),
                    new ExportCard("إجمالي الدائن (المسدد)", Amount(lines.Sum(t => t.CreditUsd), Usd), Positive: true),
                    new ExportCard("الرصيد الصافي المستحق النهائي", Amount(closing, Usd), Emphasis: true),
                ],
                Note: string.IsNullOrWhiteSpace(company.StatementNote) ? null : new ExportField("إشعار المطابقة", company.StatementNote),
                Signatures:
                [
                    new ExportSignature("إعداد المحاسب", preparedBy, preparedBy),
                    new ExportSignature("تدقيق واعتماد الإدارة", company.ManagerName, company.ManagerName is null ? null : "المدير المسؤول"),
                    new ExportSignature("مصادقة وتوقيع العميل", name, name),
                ],
                Qr: new ExportQr(links.To($"/customers/{statement.CustomerId}"), $"ACCOUNT STATEMENT\n{statement.CustomerCode}")));
    }
}

// ------------------------------------------------------------------ queries

public sealed record GetPaymentPdfQuery(Guid PaymentId) : IRequest<Result<byte[]>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Payments.Read;
}

public sealed class GetPaymentPdfQueryHandler : IRequestHandler<GetPaymentPdfQuery, Result<byte[]>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IDocumentRenderer _renderer;
    private readonly IAppLinks _links;

    public GetPaymentPdfQueryHandler(IDbConnectionFactory connectionFactory, IDocumentRenderer renderer, IAppLinks links)
    {
        _connectionFactory = connectionFactory;
        _renderer = renderer;
        _links = links;
    }

    public async Task<Result<byte[]>> Handle(GetPaymentPdfQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var payment = await connection.QuerySingleOrDefaultAsync<PrintedReceipt>(new CommandDefinition(
            """
            SELECT p.id AS Id, p.payment_number AS PaymentNumber, p.payment_type AS PaymentType, p.customer_id AS CustomerId,
                   p.payment_date AS PaymentDate, p.payment_method AS PaymentMethod, p.amount_syp AS AmountSyp, p.amount_usd AS AmountUsd,
                   p.cheque_number AS ChequeNumber, p.notes AS Notes, p.is_reversed AS IsReversed, p.reversal_reason AS ReversalReason,
                   COALESCE(NULLIF(u.full_name, ''), u.user_name) AS ReceivedByName,
                   (SELECT string_agg(i.invoice_number, '، ' ORDER BY i.invoice_date, i.invoice_number)
                    FROM payment_allocations a JOIN invoices i ON i.id = a.invoice_id WHERE a.payment_id = p.id) AS Invoices
            FROM payments p
            LEFT JOIN asp_net_users u ON u.id = COALESCE(p.received_by, p.created_by)
            WHERE p.id = @PaymentId;
            """,
            new { request.PaymentId }, cancellationToken: cancellationToken));
        if (payment is null)
        {
            return Result<byte[]>.Failure(new Error("Payment.NotFound", "Payment was not found."));
        }

        var customer = await PrintedCustomer.LoadAsync(connection, payment.CustomerId, cancellationToken);
        var company = await CompanyProfiles.LoadAsync(connection, cancellationToken);
        return Result<byte[]>.Success(_renderer.ToPdf(ReceiptPrint.Build(payment, customer, company, _links), company));
    }
}

public sealed record GetCustomerStatementPdfQuery(Guid CustomerId) : IRequest<Result<byte[]>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Reports.AccountStatement;
}

public sealed class GetCustomerStatementPdfQueryHandler : IRequestHandler<GetCustomerStatementPdfQuery, Result<byte[]>>
{
    private readonly ISender _sender;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IDocumentRenderer _renderer;
    private readonly IAppLinks _links;
    private readonly ICurrentUser _currentUser;

    public GetCustomerStatementPdfQueryHandler(ISender sender, IDbConnectionFactory connectionFactory, IDocumentRenderer renderer, IAppLinks links, ICurrentUser currentUser)
    {
        _sender = sender;
        _connectionFactory = connectionFactory;
        _renderer = renderer;
        _links = links;
        _currentUser = currentUser;
    }

    public async Task<Result<byte[]>> Handle(GetCustomerStatementPdfQuery request, CancellationToken cancellationToken)
    {
        var loaded = await _sender.Send(new GetCustomerAccountStatementQuery(request.CustomerId), cancellationToken);
        if (loaded.IsFailure)
        {
            return Result<byte[]>.Failure(loaded.Error);
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var customer = await PrintedCustomer.LoadAsync(connection, request.CustomerId, cancellationToken);
        var company = await CompanyProfiles.LoadAsync(connection, cancellationToken);
        var preparedBy = string.IsNullOrWhiteSpace(_currentUser.FullName) ? _currentUser.Username : _currentUser.FullName;
        var document = StatementPrint.Build(loaded.Value!, customer, company, _links, preparedBy, Today());
        return Result<byte[]>.Success(_renderer.ToPdf(document, company));
    }
}
