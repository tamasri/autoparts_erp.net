using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Application.Features.Printing;
using AutoPartsERP.Contracts.Common;
using AutoPartsERP.Contracts.Customers;
using AutoPartsERP.Contracts.Exports;
using AutoPartsERP.Contracts.Invoices;
using AutoPartsERP.Contracts.Settings;
using AutoPartsERP.Infrastructure.Exports;
using FluentAssertions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace AutoPartsERP.UnitTests.Printing;

/// <summary>
/// The printed forms render (valid, one-page PDFs for the usual sizes) with the sample content of the approved designs.
/// Set PRINT_PREVIEW_DIR to also write each page as a PNG, to compare with the designs by eye.
/// </summary>
public sealed class PrintedFormsTests
{
    private static readonly CompanyProfileDto Company = new(
        "اسم شركتك التجارية", "اسمك / المدير المسؤول", "123 شارع الأمل", "أي مدينة", "123-456-7890", "hello@reallygreatsite.com",
        null, null, "+963 912 345 678", "هوارد أونغ (Howard Ong)", "123-456-7890", "SA0380000000123456789012", "يعتبر هذا المستند بمثابة إشعار تسليم",
        "تعتبر البضائع والخدمات الموضحة مستلمة بحالة جيدة ومطابقة لكافة المواصفات المتفق عليها عند التوقيع.\n" +
        "يلتزم العميل بسداد كامل قيمة الفاتورة في أو قبل تاريخ استحقاق الدفع المحدد أعلاه.\n" +
        "القطع الكهربائية لا تُسترد ولا تُستبدل. الاسترجاع والاستبدال متاحان خلال 7 أيام بأصل الفاتورة، وللبضاعة بحالتها وتغليفها الأصلي.\n" +
        "لا تُعتمد أي تعديلات أو إضافات على هذه الفاتورة ما لم تكن موقعة ومختومة رسمياً من الطرفين.",
        "المبلغ المستلم غير قابل للرد إلا بموجب إشعار تسوية رسمي موقع ومختوم من الإدارة المالية.",
        "يرجى مراجعة الحركات أعلاه ومطابقة الرصيد المستحق، وفي حال وجود أي ملاحظات أو فروقات يُرجى إشعار قسم الحسابات خلال 7 أيام من تاريخه.");

    private static readonly PrintedCustomer Customer = new("C-0001", "اسم العميل الكريم", "0501234567", "123 شارع الأمل", "أي مدينة", "hello@reallygreatsite.com");

    private sealed class Links : IAppLinks
    {
        public string To(string path) => $"https://erp.example.com/{path.TrimStart('/')}";
    }

    [Fact]
    public void Sales_invoice_renders_on_one_page()
    {
        var items = new (string Code, string Name, decimal Qty, decimal Price)[]
        {
            ("JS10005", "لوحة تحكم إلكترونية ذكية", 2, 45), ("JS10023", "محول طاقة مستمر 12V 5A", 3, 15), ("JS11101", "سلك توصيل نحاسي معزول 10 متر", 5, 8.5m),
            ("JS12156", "قاطع كهربائي أوتوماتيكي 32 أمبير", 4, 12), ("JS14320", "مفتاح تشغيل صناعي مقاوم للماء", 6, 7), ("JS11650", "حساس حركة بالأشعة تحت الحمراء", 2, 18),
            ("JS19876", "مؤقت زمني رقمي قابل للبرمجة", 1, 28), ("JS60534", "شريط إضاءة LED أبيض دافئ 5 متر", 4, 11.5m), ("JS10234", "علبة توزيع كهربائية مقاومة للرطوبة", 2, 14),
            ("JS16523", "مقياس جهد رقمي متعدد الوظائف", 1, 35),
        };
        var lines = items.Select((x, i) => new InvoiceLineDto(Guid.NewGuid(), i + 1, Guid.NewGuid(), x.Code, x.Name, null, Guid.NewGuid(), x.Qty, x.Price, x.Price / 18.3m, 0,
            x.Qty * x.Price, x.Qty * x.Price / 18.3m, 0, 0, 0, false, null, null)).ToList();
        var invoice = new InvoiceDto(
            Guid.NewGuid(), "012345111", "POSTED", "SALE", Guid.NewGuid(), "C-0001", "اسم العميل الكريم", new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 16),
            352.40m, 19.26m, 0, 0, 352.40m, 19.26m, "Posted", "Sale", "", "", "", lines,
            new InvoiceAmountsDto(440.50m, 24.07m, 20, 88.10m, 4.81m, 0, 0), 0, 0, null, []);

        Render("invoice", InvoicePrint.Build(invoice, Customer, Company, new Links())).Should().Be(1);
    }

    [Fact]
    public void Receipt_voucher_renders_on_one_page()
    {
        var receipt = new PrintedReceipt(Guid.NewGuid(), "RV-2026/0142", "RECEIPT", Guid.NewGuid(), new DateOnly(2026, 2, 2), "CASH", 352.40m, 19.26m,
            null, "توريد تجهيزات كهربائية", false, null, "أحمد المحمود", "012345111");

        var document = ReceiptPrint.Build(receipt, Customer, Company, new Links());
        var words = document.Layout!.Receipt!.Lines[1].Value;
        words.Should().StartWith("فقط ").And.Contain("ليرة سورية و").And.Contain("قرشاً").And.EndWith(" لا غير");
        Render("receipt", document).Should().Be(1);
    }

    [Fact]
    public void A_receipt_paid_in_dollars_prints_dollars_and_the_dollar_words()
    {
        var receipt = new PrintedReceipt(Guid.NewGuid(), "PAY-000105", "RECEIPT", Guid.NewGuid(), new DateOnly(2026, 9, 24), "USD_CASH", 0, 50.25m,
            null, null, false, null, "أحمد المحمود", null);

        var layout = ReceiptPrint.Build(receipt, Customer, Company, new Links()).Layout!;
        layout.Receipt!.Amount.Amount.Should().Be("50.25 $");
        layout.Receipt.Amount.Equivalent.Should().BeNull();
        layout.Receipt.Lines[1].Value.Should().StartWith("فقط خمسون دولاراً أمريكياً وخمسة وعشرون سنتاً");
        layout.Receipt.Lines[2].Value.Should().Be("دفعة على الحساب");
        layout.Meta![2].Value.Should().Be("نقداً (دولار)");
    }

    [Fact]
    public void Account_statement_renders_on_one_page()
    {
        var rows = new (string Type, DateTime Date, string Reference, decimal Debit, decimal Credit)[]
        {
            ("INVOICE", new DateTime(2026, 1, 5), "INV-01234401", 210, 0), ("PAYMENT", new DateTime(2026, 1, 15), "RV-2026/0098", 0, 150),
            ("INVOICE", new DateTime(2026, 1, 24), "INV-01234488", 180, 0), ("PAYMENT", new DateTime(2026, 1, 28), "RV-2026/0122", 0, 120),
            ("INVOICE", new DateTime(2026, 2, 2), "012345111", 352.40m, 0),
        };
        decimal balance = 120;
        var transactions = rows.Select(r =>
        {
            balance += r.Debit - r.Credit;
            return new CustomerStatementTransactionDto(Guid.NewGuid(), r.Type, r.Date, null, r.Debit, r.Credit, r.Debit / 18.3m, r.Credit / 18.3m, balance, balance / 18.3m, "", r.Reference);
        }).ToList();
        var statement = new CustomerAccountStatementDto(Guid.NewGuid(), "ACC-7742", "اسم العميل الكريم", 742.40m, 40.57m, 270, 14.75m, 472.40m, 25.81m, transactions);

        Render("statement", StatementPrint.Build(statement, Customer, Company, new Links(), "سامر الحلبي", new DateOnly(2026, 2, 2))).Should().Be(1);
    }

    [Fact]
    public void A_plain_list_renders_with_the_letterhead()
    {
        var document = new ExportDocument("حركة المخزون", "من 2026-01-01 إلى 2026-01-31", [new ExportField("المستودع", "الرئيسي"), new ExportField("الصنف", "فلتر زيت")],
            [new ExportTable(null, ["التاريخ", "الحركة", "الكمية", "الرصيد"], [["2026-01-02", "استلام", "10", "10"], ["2026-01-09", "بيع", "3", "7"]], ["", "الإجمالي", "13", "7"], [2, 3])]);
        Render("list", document).Should().BeGreaterThan(0);
    }

    /// <returns>The number of pages.</returns>
    private static int Render(string name, ExportDocument document)
    {
        var pdf = new DocumentRenderer().ToPdf(document, Company);
        pdf.Length.Should().BeGreaterThan(10_000);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");

        var pages = 0;
        var dir = Environment.GetEnvironmentVariable("PRINT_PREVIEW_DIR");
        var images = DocumentRenderer.BuildPdf(document, Company).GenerateImages(new ImageGenerationSettings { RasterDpi = 110 });
        foreach (var image in images)
        {
            pages++;
            if (!string.IsNullOrWhiteSpace(dir))
            {
                File.WriteAllBytes(Path.Combine(dir, $"{name}-{pages}.png"), image);
            }
        }

        if (!string.IsNullOrWhiteSpace(dir))
        {
            File.WriteAllBytes(Path.Combine(dir, $"{name}.pdf"), pdf);
        }

        return pages;
    }
}
