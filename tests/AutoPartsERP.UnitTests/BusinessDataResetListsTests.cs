using AutoPartsERP.Infrastructure.Maintenance;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class BusinessDataResetListsTests
{
    [Fact]
    public void A_table_is_either_wiped_or_kept_never_both()
    {
        Assert.Empty(BusinessDataReset.Wiped.Intersect(BusinessDataReset.Kept));
        Assert.Equal(BusinessDataReset.Wiped.Count, BusinessDataReset.Wiped.Distinct().Count());
        Assert.Equal(BusinessDataReset.Kept.Count, BusinessDataReset.Kept.Distinct().Count());
    }

    [Theory]
    [InlineData("asp_net_users")]
    [InlineData("asp_net_roles")]
    [InlineData("__EFMigrationsHistory")]
    [InlineData("reason_codes")]
    public void Installation_tables_are_kept(string table) => Assert.Contains(table, BusinessDataReset.Kept);
}
