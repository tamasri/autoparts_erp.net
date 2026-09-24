using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// The company's own details printed on every document (issuer block, footer contacts, bank details, manager's name) and the
/// default texts of the printed forms. A single row (id = 1), created here with the texts of the approved designs.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000030_AddCompanyProfile")]
public sealed class AddCompanyProfile : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE company_profile (
                id               smallint PRIMARY KEY DEFAULT 1 CHECK (id = 1),
                name             text NOT NULL,
                manager_name     text,
                address          text,
                city             text,
                phone            text,
                email            text,
                website          text,
                tax_number       text,
                whatsapp         text,
                bank_name        text,
                bank_account     text,
                iban             text,
                invoice_subtitle text,
                invoice_terms    text,
                receipt_note     text,
                statement_note   text,
                updated_at       timestamptz NOT NULL DEFAULT now(),
                updated_by       uuid
            );

            INSERT INTO company_profile (id, name, invoice_subtitle, invoice_terms, receipt_note, statement_note) VALUES (
                1,
                'AutoParts ERP',
                'يعتبر هذا المستند بمثابة إشعار تسليم',
                E'تعتبر البضائع والخدمات الموضحة مستلمة بحالة جيدة ومطابقة لكافة المواصفات المتفق عليها عند التوقيع.\n'
                || E'يلتزم العميل بسداد كامل قيمة الفاتورة في أو قبل تاريخ استحقاق الدفع المحدد أعلاه.\n'
                || E'الاسترجاع والاستبدال متاحان خلال 7 أيام بأصل الفاتورة، وللبضاعة بحالتها وتغليفها الأصلي.\n'
                || 'لا تُعتمد أي تعديلات أو إضافات على هذه الفاتورة ما لم تكن موقعة ومختومة رسمياً من الطرفين.',
                'المبلغ المستلم غير قابل للرد إلا بموجب إشعار تسوية رسمي موقع ومختوم من الإدارة المالية.',
                'يرجى مراجعة الحركات أعلاه ومطابقة الرصيد المستحق، وفي حال وجود أي ملاحظات أو فروقات يُرجى إشعار قسم الحسابات خلال 7 أيام من تاريخه.'
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS company_profile;");
    }
}
