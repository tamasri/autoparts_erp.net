namespace AutoPartsERP.Contracts.Exports;

/// <summary>One "label: value" line in the header block of a document (customer, date, warehouse, ...).</summary>
public sealed record ExportField(string Label, string? Value);

/// <summary>A table of a document. <c>NumericColumns</c> are 0-based indexes whose cells are numbers (right aligned, numeric in Excel).</summary>
public sealed record ExportTable(
    string? Title,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    IReadOnlyList<string?>? Totals = null,
    IReadOnlyList<int>? NumericColumns = null);

/// <summary>Everything needed to print or export a document or a list: rendered as PDF, Excel or CSV by the same engine.</summary>
public sealed record ExportDocument(
    string Title,
    string? Subtitle,
    IReadOnlyList<ExportField> Fields,
    IReadOnlyList<ExportTable> Tables,
    string? Footer = null,
    string? FileName = null);
