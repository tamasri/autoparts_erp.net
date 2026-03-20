using ClosedXML.Excel;

namespace AutoPartsERP.Infrastructure.Exports;

public sealed class AccountStatementExcelExporter
{
    public byte[] Export(
        string customerCode,
        string customerName,
        IReadOnlyCollection<(DateTime Date, string Type, decimal DebitSyp, decimal CreditSyp, decimal BalanceSyp)> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Account Statement");
        sheet.Cell(1, 1).Value = "Customer";
        sheet.Cell(1, 2).Value = $"{customerCode} - {customerName}";
        sheet.Cell(3, 1).Value = "Date";
        sheet.Cell(3, 2).Value = "Type";
        sheet.Cell(3, 3).Value = "Debit SYP";
        sheet.Cell(3, 4).Value = "Credit SYP";
        sheet.Cell(3, 5).Value = "Balance SYP";

        var rowIndex = 4;
        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).Value = row.Date;
            sheet.Cell(rowIndex, 2).Value = row.Type;
            sheet.Cell(rowIndex, 3).Value = row.DebitSyp;
            sheet.Cell(rowIndex, 4).Value = row.CreditSyp;
            sheet.Cell(rowIndex, 5).Value = row.BalanceSyp;
            rowIndex++;
        }

        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
