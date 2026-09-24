using System.Globalization;
using System.Reflection;
using System.Text;
using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Contracts.Exports;
using AutoPartsERP.Contracts.Settings;
using ClosedXML.Excel;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AutoPartsERP.Infrastructure.Exports;

/// <summary>
/// One engine for every printed document and exported list. PDFs use embedded fonts (Tajawal for Arabic, IBM Plex Mono for
/// figures, Noto Sans as fallback) so they render the same on the Linux server as on a developer machine; the layout is PdfLayout.
/// </summary>
public sealed class DocumentRenderer : IDocumentRenderer
{
    private static readonly object FontLock = new();
    private static bool _fontsRegistered;

    public byte[] ToPdf(ExportDocument document, CompanyProfileDto company) => BuildPdf(document, company).GeneratePdf();

    /// <summary>The PDF as a QuestPDF document (also used to rasterise pages when checking the layout).</summary>
    public static IDocument BuildPdf(ExportDocument document, CompanyProfileDto company)
    {
        EnsureFonts();
        return Document.Create(container => PdfLayout.Compose(container, document, company))
            .WithMetadata(new DocumentMetadata { Title = document.Title, Author = company.Name, Creator = "AutoParts ERP", Producer = "AutoParts ERP" });
    }

    public byte[] ToXlsx(ExportDocument document)
    {
        using var workbook = new XLWorkbook();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        var tables = document.Tables.Count > 0 ? document.Tables : new List<ExportTable> { new(null, [], []) };

        foreach (var t in tables)
        {
            index++;
            var sheet = workbook.Worksheets.Add(UniqueSheetName(t.Title ?? document.Title, index, used));
            sheet.RightToLeft = true;

            var row = 1;
            if (index == 1)
            {
                sheet.Cell(row, 1).Value = document.Title;
                sheet.Cell(row, 1).Style.Font.Bold = true;
                sheet.Cell(row, 1).Style.Font.FontSize = 14;
                row++;
                if (!string.IsNullOrWhiteSpace(document.Subtitle)) { sheet.Cell(row++, 1).Value = document.Subtitle; }
                foreach (var f in document.Fields)
                {
                    sheet.Cell(row, 1).Value = f.Label;
                    sheet.Cell(row, 1).Style.Font.Bold = true;
                    sheet.Cell(row, 2).Value = f.Value ?? string.Empty;
                    row++;
                }

                row++;
            }

            for (var c = 0; c < t.Columns.Count; c++)
            {
                var cell = sheet.Cell(row, c + 1);
                cell.Value = t.Columns[c];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#054239");
            }

            row++;
            foreach (var r in t.Rows)
            {
                for (var c = 0; c < t.Columns.Count; c++) { WriteCell(sheet.Cell(row, c + 1), t, c, c < r.Count ? r[c] : null); }
                row++;
            }

            if (t.Totals is { Count: > 0 })
            {
                for (var c = 0; c < t.Columns.Count; c++)
                {
                    WriteCell(sheet.Cell(row, c + 1), t, c, c < t.Totals.Count ? t.Totals[c] : null);
                    sheet.Cell(row, c + 1).Style.Font.Bold = true;
                }
            }

            sheet.Columns().AdjustToContents(1, Math.Min(row, 500), 8, 60);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] ToCsv(ExportDocument document)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Csv(document.Title));
        foreach (var f in document.Fields) { sb.AppendLine($"{Csv(f.Label)},{Csv(f.Value)}"); }
        foreach (var t in document.Tables)
        {
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(t.Title)) { sb.AppendLine(Csv(t.Title)); }
            sb.AppendLine(string.Join(',', t.Columns.Select(Csv)));
            foreach (var r in t.Rows) { sb.AppendLine(string.Join(',', Enumerable.Range(0, t.Columns.Count).Select(i => Csv(i < r.Count ? r[i] : null)))); }
            if (t.Totals is { Count: > 0 }) { sb.AppendLine(string.Join(',', Enumerable.Range(0, t.Columns.Count).Select(i => Csv(i < t.Totals.Count ? t.Totals[i] : null)))); }
        }

        // UTF-8 BOM so Excel opens Arabic text correctly.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static bool IsNumeric(ExportTable t, int column) => t.NumericColumns?.Contains(column) == true;

    private static string Display(ExportTable t, int column, string? value)
    {
        if (string.IsNullOrEmpty(value)) { return string.Empty; }
        if (IsNumeric(t, column) && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number == decimal.Truncate(number) ? number.ToString("N0", CultureInfo.InvariantCulture) : number.ToString("N2", CultureInfo.InvariantCulture);
        }

        return value;
    }

    private static void WriteCell(IXLCell cell, ExportTable t, int column, string? value)
    {
        if (string.IsNullOrEmpty(value)) { return; }
        if (IsNumeric(t, column) && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            cell.Value = number;
            cell.Style.NumberFormat.Format = "#,##0.00";
            return;
        }

        cell.Value = NeutraliseFormula(value);
    }

    /// <summary>Text that starts with = + - @ would be run as a formula by Excel; prefix it so user-entered data can never execute.</summary>
    private static string NeutraliseFormula(string value) =>
        value.Length > 0 && "=+-@".Contains(value[0]) && !decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            ? "'" + value
            : value;

    private static string UniqueSheetName(string raw, int index, HashSet<string> used)
    {
        var name = new string(raw.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()).Trim();
        if (name.Length == 0) { name = $"Sheet{index}"; }
        if (name.Length > 28) { name = name[..28]; }
        var candidate = name;
        for (var n = 2; !used.Add(candidate); n++) { candidate = $"{name}-{n}"; }
        return candidate;
    }

    private static string Csv(string? value)
    {
        var v = NeutraliseFormula(value ?? string.Empty);
        return v.Contains(',') || v.Contains('"') || v.Contains('\n') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    private static void EnsureFonts()
    {
        lock (FontLock)
        {
            if (_fontsRegistered) { return; }
            QuestPDF.Settings.License = LicenseType.Community;
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = assembly.GetManifestResourceStream(resource)!;
                FontManager.RegisterFont(stream);
            }

            _fontsRegistered = true;
        }
    }
}
