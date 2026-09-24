namespace AutoPartsERP.Contracts.Exports;

/// <summary>One "label: value" line in the header block of a document (customer, date, warehouse, ...).</summary>
public sealed record ExportField(string Label, string? Value);

/// <summary>
/// A table of a document. <c>NumericColumns</c> are 0-based indexes whose cells are numbers (right aligned, numeric in Excel).
/// The optional parts only change how the PDF looks: <c>MoneyColumns</c> print with two decimals and <c>Currency</c> after them,
/// <c>CodeColumns</c> print in the monospaced face, <c>PositiveColumns</c> in green, <c>ShadedColumn</c> on a grey band, and
/// <c>Widths</c> are relative column widths.
/// </summary>
public sealed record ExportTable(
    string? Title,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    IReadOnlyList<string?>? Totals = null,
    IReadOnlyList<int>? NumericColumns = null,
    IReadOnlyList<int>? MoneyColumns = null,
    string? Currency = null,
    IReadOnlyList<int>? CodeColumns = null,
    IReadOnlyList<int>? PositiveColumns = null,
    int? ShadedColumn = null,
    IReadOnlyList<float>? Widths = null);

/// <summary>Everything needed to print or export a document or a list: rendered as PDF, Excel or CSV by the same engine.</summary>
public sealed record ExportDocument(
    string Title,
    string? Subtitle,
    IReadOnlyList<ExportField> Fields,
    IReadOnlyList<ExportTable> Tables,
    string? Footer = null,
    string? FileName = null,
    ExportLayout? Layout = null);

/// <summary>
/// The printed form of an official document (invoice, receipt voucher, account statement...), on top of the plain fields and
/// tables. Every part is optional and drawn in this order: meta line, parties, opening bar, receipt body, tables, summary and
/// total, cards, note, terms beside the recipient box, signatures, and the footer (payment details, QR codes, contacts).
/// The company's own details (name, address, contacts, bank) come from the company profile at render time.
/// </summary>
public sealed record ExportLayout(
    IReadOnlyList<ExportMeta>? Meta = null,
    IReadOnlyList<ExportParty>? Parties = null,
    ExportField? Opening = null,
    ExportReceipt? Receipt = null,
    IReadOnlyList<ExportField>? Summary = null,
    ExportAmount? Total = null,
    IReadOnlyList<ExportCard>? Cards = null,
    ExportField? Note = null,
    IReadOnlyList<string>? Terms = null,
    bool RecipientBox = false,
    IReadOnlyList<ExportSignature>? Signatures = null,
    bool Stamp = false,
    bool PaymentDetails = false,
    ExportQr? Qr = null);

/// <summary>An item of the line under the title (number, dates). <c>Badge</c> prints the value on a dark chip.</summary>
public sealed record ExportMeta(string Label, string Value, bool Badge = false);

/// <summary>
/// A party block ("invoice to", "issued by"). With <c>Company</c> the name and lines are the company profile's.
/// <c>Accent</c> draws a bar beside the block; <c>Icon</c> is one of: user, building.
/// </summary>
public sealed record ExportParty(string Title, string? Name = null, IReadOnlyList<string>? Lines = null, bool Company = false, bool Accent = false, string? Icon = null);

/// <summary>An amount with an optional equivalent in another currency (e.g. "≈ 19.26 $").</summary>
public sealed record ExportAmount(string Label, string Amount, string? Equivalent = null);

/// <summary>A totals card of an account statement. <c>Emphasis</c> is the dark card; <c>Positive</c> prints the amount in green.</summary>
public sealed record ExportCard(string Label, string Amount, string? Equivalent = null, bool Emphasis = false, bool Positive = false);

/// <summary>The body of a receipt voucher: the amount in figures, then "received from", "the sum of (in words)", "for".</summary>
public sealed record ExportReceipt(ExportAmount Amount, IReadOnlyList<ExportField> Lines, int? WordsLine = null);

/// <summary>A signature box: its title, the name printed in it, and a small caption under the name.</summary>
public sealed record ExportSignature(string Title, string? Name = null, string? Caption = null);

/// <summary>The document's verification QR code (a link to the document) with a caption beside it.</summary>
public sealed record ExportQr(string Url, string? Caption = null);
