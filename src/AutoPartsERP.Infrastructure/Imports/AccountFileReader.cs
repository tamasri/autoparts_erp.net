using AutoPartsERP.Contracts.Accounting;

namespace AutoPartsERP.Infrastructure.Imports;

/// <summary>Reads a chart-of-accounts file (.xlsx or .csv), tolerating Arabic or English column titles, and builds the blank template.</summary>
public static class AccountFileReader
{
    private static readonly string[] TemplateHeaders = ["AccountName", "Parent", "IsGroup", "AccountType", "AccountNumber"];

    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["AccountName"] = ["accountname", "account_name", "account name", "name", "الاسم", "اسم الحساب", "الحساب"],
        ["Parent"] = ["parent", "parentaccount", "parent_account", "parent account", "الحساب الأب", "الأب", "المجموعة الأم", "الحساب الرئيسي"],
        ["IsGroup"] = ["isgroup", "is_group", "is group", "group", "type", "مجموعة", "مجموعة؟", "النوع", "نوع العقدة"],
        ["AccountType"] = ["accounttype", "account_type", "account type", "نوع الحساب"],
        ["AccountNumber"] = ["accountnumber", "account_number", "account number", "number", "no", "رقم الحساب", "الرقم", "رقم"]
    };

    private static readonly string[] Yes = ["1", "true", "yes", "y", "group", "نعم", "مجموعة"];
    private static readonly string[] No = ["0", "false", "no", "n", "ledger", "لا", "حساب", "حساب حركة", "دفتر"];

    public static IReadOnlyList<ImportAccountRow> Read(Stream stream, string fileName)
    {
        var table = TabularFile.Read(stream, fileName);
        if (table.Count == 0)
        {
            return [];
        }

        var headerIndex = table.FindIndex(r => r.Any(c => TabularFile.Canonical(Aliases, c) == "AccountName"));
        if (headerIndex < 0)
        {
            throw new InvalidDataException("لم يتم العثور على عمود اسم الحساب (AccountName). استخدم القالب الجاهز.");
        }

        var map = TabularFile.MapHeader(Aliases, table[headerIndex]);
        var rows = new List<ImportAccountRow>();
        for (var r = headerIndex + 1; r < table.Count; r++)
        {
            var cells = table[r];
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            string? Text(string key) => map.TryGetValue(key, out var i) && i < cells.Count && !string.IsNullOrWhiteSpace(cells[i]) ? cells[i].Trim() : null;

            string? error = null;
            bool? isGroup = null;
            if (Text("IsGroup") is { } flag)
            {
                var lowered = flag.ToLowerInvariant();
                if (Yes.Contains(lowered)) isGroup = true;
                else if (No.Contains(lowered)) isGroup = false;
                else error = $"قيمة غير مفهومة في عمود المجموعة: {flag}";
            }

            rows.Add(new ImportAccountRow(r + 1, Text("AccountName"), Text("Parent"), isGroup, Text("AccountType"), Text("AccountNumber"), error));
        }

        return rows;
    }

    public static byte[] BuildTemplateXlsx() => TabularFile.BuildXlsx(
        "Accounts", TemplateHeaders, ["Petty Cash", "Cash In Hand - AB", "0", "Cash", ""],
        "AccountName إلزامي. Parent هو اسم الحساب الأب كما يظهر في شجرة الحسابات (يجوز أن يكون حساباً أُنشئ في الملف نفسه قبل هذا السطر).\nIsGroup: 1 لمجموعة، 0 لحساب حركة (الافتراضي حساب حركة). AccountType اختياري لحسابات الحركة (Cash, Bank, Receivable, Payable, ...).\nالحسابات الموجودة مسبقاً تُتجاوز ولا تُعدَّل.");

    public static byte[] BuildTemplateCsv() => TabularFile.BuildCsv(TemplateHeaders, "Petty Cash,Cash In Hand - AB,0,Cash,");
}
