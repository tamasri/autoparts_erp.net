using System.Text;
using AutoPartsERP.Infrastructure.Imports;
using FluentAssertions;
using Xunit;

namespace AutoPartsERP.UnitTests;

public sealed class ItemFileReaderTests
{
    [Fact]
    public void Extra_numbers_and_tags_are_read_from_their_Arabic_columns()
    {
        const string csv = "رقم القطعة,الاسم,الفئة,سعر البيع $,الأرقام الإضافية,الوسوم,سعر الشراء $ (للمرجع)\n" +
                           "JS10003,كولية أمامي تويوتا هايسي 2004,فرامل,13,SP1376 | 04465-26420 | sp1376,تويوتا | لكزس,7.5\n";

        var rows = ItemFileReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(csv)), "items.csv");

        rows.Should().ContainSingle();
        var row = rows[0];
        row.ParseError.Should().BeNull();
        row.Code.Should().Be("JS10003");
        row.NameAr.Should().Be("كولية أمامي تويوتا هايسي 2004");
        row.PriceUsd.Should().Be(13m);
        row.Aliases.Should().Equal("SP1376", "04465-26420");
        row.Tags.Should().Equal("تويوتا", "لكزس");
    }

    [Fact]
    public void A_file_without_the_new_columns_reads_as_before()
    {
        const string csv = "Code,NameAr,PriceUsd\nOIL-1,فلتر زيت,9\n";

        var row = ItemFileReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(csv)), "items.csv").Single();

        row.Aliases.Should().BeNull();
        row.Tags.Should().BeNull();
        row.PriceUsd.Should().Be(9m);
    }
}
