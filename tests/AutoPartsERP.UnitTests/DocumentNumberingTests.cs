using AutoPartsERP.Application.Features.Documents;
using AutoPartsERP.Domain.Constants;
using AutoPartsERP.Infrastructure.Maintenance;
using AutoPartsERP.Infrastructure.Persistence.Migrations;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class DocumentNumberingTests
{
    [Fact]
    public void The_api_kinds_and_the_database_triggers_cover_the_same_tables_and_number_columns()
    {
        var api = NumberedDocuments.All.Select(k => (k.Table, k.NumberColumn)).OrderBy(x => x.Table);
        var database = AddDocumentNumbering.NumberedTables.Select(t => (t.Table, t.NumberColumn)).OrderBy(x => x.Table);

        api.Should().Equal(database);
    }

    [Fact]
    public void Every_numbered_table_and_the_numbering_tables_are_wiped_by_the_business_data_reset()
    {
        BusinessDataReset.Wiped.Should().Contain(NumberedDocuments.All.Select(k => k.Table));
        BusinessDataReset.Wiped.Should().Contain(["document_series", "deleted_documents"]);
    }

    [Fact]
    public void Only_invoices_purchase_invoices_and_journal_entries_can_be_deleted()
    {
        NumberedDocuments.All.Where(k => k.Deletion is not null).Select(k => k.Kind)
            .Should().BeEquivalentTo(NumberedDocuments.Invoices, NumberedDocuments.PurchaseInvoices, NumberedDocuments.JournalEntries);
    }

    [Fact]
    public void The_reserved_delete_permission_belongs_to_the_super_admin_role_only()
    {
        PermissionCodes.Reserved.Should().Contain(PermissionCodes.Documents.Delete);
        RolePermissionMap.Bundles[RoleCodes.SystemAdministrator].Should().Contain(PermissionCodes.Reserved);
        RolePermissionMap.Bundles.Where(b => b.Key != RoleCodes.SystemAdministrator)
            .Where(b => b.Value.Intersect(PermissionCodes.Reserved).Any()).Select(b => b.Key)
            .Should().BeEmpty("a reserved permission is the Super Admin's only");
    }
}
