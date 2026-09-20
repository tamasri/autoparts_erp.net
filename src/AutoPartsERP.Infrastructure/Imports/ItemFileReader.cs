using System.Globalization;
using AutoPartsERP.Contracts.Items;

namespace AutoPartsERP.Infrastructure.Imports;

/// <summary>Reads an item import file (.xlsx or .csv) into rows, tolerating English or Arabic column titles, and builds the blank template.</summary>
public static class ItemFileReader
{
    private static readonly string[] TemplateHeaders = ["Code", "Name", "NameAr", "Category", "Barcode", "PriceUsd", "PriceSyp", "MinPriceUsd", "WarrantyMonths"];

    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["Code"] = ["code", "sku", "partnumber", "part number", "part_number", "رقم القطعة", "رمز", "الرمز", "الكود", "كود"],
        ["Name"] = ["name", "nameen", "name_en", "name en", "english name", "الاسم بالانجليزي", "الاسم بالإنجليزية", "الاسم الانجليزي"],
        ["NameAr"] = ["namear", "name_ar", "name ar", "arabic name", "الاسم", "الاسم بالعربية", "الاسم العربي"],
        ["Category"] = ["category", "الفئة", "التصنيف", "القسم"],
        ["Barcode"] = ["barcode", "الباركود", "باركود"],
        ["PriceUsd"] = ["priceusd", "price_usd", "price usd", "price", "سعر البيع $", "سعر البيع بالدولار", "السعر بالدولار", "السعر"],
        ["PriceSyp"] = ["pricesyp", "price_syp", "price syp", "سعر البيع ل.س", "السعر بالليرة", "السعر بالليرة السورية"],
        ["MinPriceUsd"] = ["minpriceusd", "min_price_usd", "min price", "الحد الأدنى للسعر", "أدنى سعر"],
        ["WarrantyMonths"] = ["warrantymonths", "warranty_months", "warranty", "الضمان", "مدة الضمان"]
    };

    public static IReadOnlyList<ImportItemRow> Read(Stream stream, string fileName)
    {
        var table = TabularFile.Read(stream, fileName);
        if (table.Count == 0)
        {
            return [];
        }

        // The first row that contains a recognised code column is the header (a title row above it is tolerated).
        var headerIndex = table.FindIndex(r => r.Any(c => TabularFile.Canonical(Aliases, c) == "Code"));
        if (headerIndex < 0)
        {
            throw new InvalidDataException("لم يتم العثور على عمود الرمز (Code). استخدم القالب الجاهز.");
        }

        var map = TabularFile.MapHeader(Aliases, table[headerIndex]);

        var rows = new List<ImportItemRow>();
        for (var r = headerIndex + 1; r < table.Count; r++)
        {
            var cells = table[r];
            if (cells.All(string.IsNullOrWhiteSpace)) continue;

            string? Text(string key) => map.TryGetValue(key, out var i) && i < cells.Count && !string.IsNullOrWhiteSpace(cells[i]) ? cells[i].Trim() : null;
            string? error = null;

            decimal? Number(string key)
            {
                var raw = Text(key);
                if (raw is null) return null;
                if (decimal.TryParse(raw.Replace(",", string.Empty).Replace("٫", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
                error ??= $"قيمة رقمية غير صالحة في '{key}': {raw}";
                return null;
            }

            var warranty = Number("WarrantyMonths");
            rows.Add(new ImportItemRow(r + 1, Text("Code"), Text("Name"), Text("NameAr"), Text("Category"), Text("Barcode"),
                Number("PriceUsd"), Number("PriceSyp"), Number("MinPriceUsd"), warranty is null ? null : (int)warranty, error));
        }

        return rows;
    }

    public static byte[] BuildTemplateXlsx() => TabularFile.BuildXlsx(
        "Items", TemplateHeaders, ["OIL-FILT-001", "Oil filter Toyota", "فلتر زيت تويوتا", "فلاتر", "6291234567890", 12.5, "", 9, 6],
        "Code إلزامي. Name أو NameAr إلزامي (أحدهما يكفي). الفئة تُطابق اسم فئة موجودة (وإلا تُستخدم أول فئة).\nPriceSyp اختياري: إن تُرك فارغاً يُحسب من PriceUsd بآخر سعر صرف. الأصناف الموجودة مسبقاً تُتجاوز ولا تُعدَّل.");

    public static byte[] BuildTemplateCsv() => TabularFile.BuildCsv(TemplateHeaders, "OIL-FILT-001,Oil filter Toyota,فلتر زيت تويوتا,فلاتر,6291234567890,12.5,,9,6");
}
