using ClosedXML.Excel;

namespace AutoPartsERP.Infrastructure.Exports;

public sealed class InventoryExcelExporter
{
    public byte[] Export(IReadOnlyCollection<(string SkuCode, string SkuName, decimal Quantity, decimal CostSyp, decimal CostUsd)> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Inventory Value");
        sheet.Cell(1, 1).Value = "SKU";
        sheet.Cell(1, 2).Value = "Name";
        sheet.Cell(1, 3).Value = "Quantity";
        sheet.Cell(1, 4).Value = "Cost SYP";
        sheet.Cell(1, 5).Value = "Cost USD";

        var rowIndex = 2;
        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).Value = row.SkuCode;
            sheet.Cell(rowIndex, 2).Value = row.SkuName;
            sheet.Cell(rowIndex, 3).Value = row.Quantity;
            sheet.Cell(rowIndex, 4).Value = row.CostSyp;
            sheet.Cell(rowIndex, 5).Value = row.CostUsd;
            rowIndex++;
        }

        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
