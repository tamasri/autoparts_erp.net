using System.Globalization;
using AutoPartsERP.Contracts.Items;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;

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
        var table = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ReadCsv(stream) : ReadXlsx(stream);
        if (table.Count == 0)
        {
            return [];
        }

        // The first row that contains a recognised code column is the header (a title row above it is tolerated).
        var headerIndex = table.FindIndex(r => r.Any(c => Canonical(c) == "Code"));
        if (headerIndex < 0)
        {
            throw new InvalidDataException("لم يتم العثور على عمود الرمز (Code). استخدم القالب الجاهز.");
        }

        var map = new Dictionary<string, int>();
        for (var i = 0; i < table[headerIndex].Count; i++)
        {
            var key = Canonical(table[headerIndex][i]);
            if (key is not null && !map.ContainsKey(key)) map[key] = i;
        }

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

    public static byte[] BuildTemplateXlsx()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Items");
        for (var c = 0; c < TemplateHeaders.Length; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = TemplateHeaders[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#5c54ff");
            cell.Style.Font.FontColor = XLColor.White;
        }

        var sample = new object[] { "OIL-FILT-001", "Oil filter Toyota", "فلتر زيت تويوتا", "فلاتر", "6291234567890", 12.5, "", 9, 6 };
        for (var c = 0; c < sample.Length; c++)
        {
            if (sample[c] is string s) sheet.Cell(2, c + 1).Value = s; else if (sample[c] is double d) sheet.Cell(2, c + 1).Value = d; else if (sample[c] is int n) sheet.Cell(2, c + 1).Value = n;
        }

        var note = workbook.Worksheets.Add("ملاحظات");
        note.RightToLeft = true;
        note.Cell(1, 1).Value = "Code إلزامي. Name أو NameAr إلزامي (أحدهما يكفي). الفئة تُطابق اسم فئة موجودة (وإلا تُستخدم أول فئة).";
        note.Cell(2, 1).Value = "PriceSyp اختياري: إن تُرك فارغاً يُحسب من PriceUsd بآخر سعر صرف. الأصناف الموجودة مسبقاً تُتجاوز ولا تُعدَّل.";
        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] BuildTemplateCsv()
    {
        var text = string.Join(',', TemplateHeaders) + "\nOIL-FILT-001,Oil filter Toyota,فلتر زيت تويوتا,فلاتر,6291234567890,12.5,,9,6\n";
        return [.. System.Text.Encoding.UTF8.GetPreamble(), .. System.Text.Encoding.UTF8.GetBytes(text)];
    }

    private static string? Canonical(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        var h = header.Trim().ToLowerInvariant();
        foreach (var (key, names) in Aliases)
        {
            if (names.Any(n => string.Equals(n, h, StringComparison.OrdinalIgnoreCase)) || string.Equals(key, header.Trim(), StringComparison.OrdinalIgnoreCase)) return key;
        }

        return null;
    }

    private static List<List<string>> ReadXlsx(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var range = sheet.RangeUsed();
        var table = new List<List<string>>();
        if (range is null) return table;

        foreach (var row in range.Rows())
        {
            table.Add(Enumerable.Range(1, range.ColumnCount()).Select(c => row.Cell(c).GetFormattedString()).ToList());
        }

        return table;
    }

    private static List<List<string>> ReadCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = false, BadDataFound = null, MissingFieldFound = null, DetectDelimiter = true };
        using var csv = new CsvReader(reader, config);
        var table = new List<List<string>>();
        while (csv.Read())
        {
            var cells = new List<string>();
            for (var i = 0; i < csv.Parser.Count; i++) cells.Add(csv.GetField(i) ?? string.Empty);
            table.Add(cells);
        }

        return table;
    }
}
