namespace AutoPartsERP.Contracts.Settings;

/// <summary>
/// The company's own details printed on every document: the issuer block, the footer contacts, the bank details under an invoice,
/// the manager's name in the approval box, and the default texts (invoice terms, receipt note, statement note).
/// <c>InvoiceTerms</c> holds one term per line.
/// </summary>
public sealed record CompanyProfileDto(
    string Name,
    string? ManagerName,
    string? Address,
    string? City,
    string? Phone,
    string? Email,
    string? Website,
    string? TaxNumber,
    string? WhatsApp,
    string? BankName,
    string? BankAccount,
    string? Iban,
    string? InvoiceSubtitle,
    string? InvoiceTerms,
    string? ReceiptNote,
    string? StatementNote,
    DateTime? UpdatedAt = null)
{
    /// <summary>What prints when nothing has been entered yet.</summary>
    public static CompanyProfileDto Empty { get; } = new("AutoParts ERP", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
}
