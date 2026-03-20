using ClosedXML.Excel;

namespace AutoPartsERP.Infrastructure.Exports;

public sealed class ProfitLossExcelExporter
{
    public byte[] Export(IReadOnlyCollection<(int Year, int Month, decimal RevenueSyp, decimal CogsSyp, decimal GrossProfitSyp, decimal MarginPct)> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Profit Loss");
        sheet.Cell(1, 1).Value = "Year";
        sheet.Cell(1, 2).Value = "Month";
        sheet.Cell(1, 3).Value = "Revenue SYP";
        sheet.Cell(1, 4).Value = "COGS SYP";
        sheet.Cell(1, 5).Value = "Gross Profit SYP";
        sheet.Cell(1, 6).Value = "Margin %";

        var rowIndex = 2;
        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).Value = row.Year;
            sheet.Cell(rowIndex, 2).Value = row.Month;
            sheet.Cell(rowIndex, 3).Value = row.RevenueSyp;
            sheet.Cell(rowIndex, 4).Value = row.CogsSyp;
            sheet.Cell(rowIndex, 5).Value = row.GrossProfitSyp;
            sheet.Cell(rowIndex, 6).Value = row.MarginPct;
            rowIndex++;
        }

        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
