using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;

namespace AutoPartsERP.Infrastructure.Imports;

/// <summary>What every import reader needs: a .xlsx or .csv file as rows of text, and column titles matched to known names in Arabic or English.</summary>
internal static class TabularFile
{
    public static List<List<string>> Read(Stream stream, string fileName) =>
        fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ReadCsv(stream) : ReadXlsx(stream);

    /// <summary>The known column a title stands for (by alias or by its own name), or null when it is not one of them.</summary>
    public static string? Canonical(IReadOnlyDictionary<string, string[]> aliases, string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        var h = header.Trim().ToLowerInvariant();
        foreach (var (key, names) in aliases)
        {
            if (names.Any(n => string.Equals(n, h, StringComparison.OrdinalIgnoreCase)) || string.Equals(key, header.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>Maps each known column to its index in the header row.</summary>
    public static Dictionary<string, int> MapHeader(IReadOnlyDictionary<string, string[]> aliases, IReadOnlyList<string> headerRow)
    {
        var map = new Dictionary<string, int>();
        for (var i = 0; i < headerRow.Count; i++)
        {
            var key = Canonical(aliases, headerRow[i]);
            if (key is not null && !map.ContainsKey(key))
            {
                map[key] = i;
            }
        }

        return map;
    }

    public static byte[] BuildXlsx(string sheetName, string[] headers, object[] sample, string? noteSheetText)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#5c54ff");
            cell.Style.Font.FontColor = XLColor.White;
        }

        for (var c = 0; c < sample.Length; c++)
        {
            switch (sample[c])
            {
                case string s: sheet.Cell(2, c + 1).Value = s; break;
                case double d: sheet.Cell(2, c + 1).Value = d; break;
                case int n: sheet.Cell(2, c + 1).Value = n; break;
            }
        }

        if (noteSheetText is not null)
        {
            var note = workbook.Worksheets.Add("ملاحظات");
            note.RightToLeft = true;
            var row = 1;
            foreach (var line in noteSheetText.Split('\n'))
            {
                note.Cell(row++, 1).Value = line;
            }
        }

        sheet.Columns().AdjustToContents();
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    public static byte[] BuildCsv(string[] headers, string sampleLine)
    {
        var text = string.Join(',', headers) + "\n" + sampleLine + "\n";
        return [.. System.Text.Encoding.UTF8.GetPreamble(), .. System.Text.Encoding.UTF8.GetBytes(text)];
    }

    private static List<List<string>> ReadXlsx(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var range = sheet.RangeUsed();
        var table = new List<List<string>>();
        if (range is null)
        {
            return table;
        }

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
            for (var i = 0; i < csv.Parser.Count; i++)
            {
                cells.Add(csv.GetField(i) ?? string.Empty);
            }

            table.Add(cells);
        }

        return table;
    }
}
