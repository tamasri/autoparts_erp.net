using FluentAssertions;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class PartyMigrationTests
{
    [Fact]
    public void ExistingCustomers_GetPartyRows_OnMigration()
    {
        var migrationFile = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "AutoPartsERP.Infrastructure", "Persistence", "Migrations", "AddPartyCoreAndOutbox.cs");

        File.Exists(migrationFile).Should().BeTrue();
        var source = File.ReadAllText(migrationFile);

        source.Should().Contain("INSERT INTO parties", "existing customers must be backfilled into parties.");
        source.Should().Contain("UPDATE customers", "each customer row should receive party_id.");
        source.Should().Contain("ALTER TABLE customers ALTER COLUMN party_id SET NOT NULL");
    }
}
