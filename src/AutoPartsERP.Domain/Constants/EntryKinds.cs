namespace AutoPartsERP.Domain.Constants;

/// <summary>
/// What an accounting entry type does. Users can define as many entry types as they like (each with its own name and number prefix),
/// but every type belongs to one of these kinds, which decides the ERPNext voucher type the entry is booked as.
/// </summary>
public static class EntryKinds
{
    public const string Receipt = "RECEIPT";
    public const string Payment = "PAYMENT";
    public const string Contra = "CONTRA";
    public const string Journal = "JOURNAL";
    public const string Opening = "OPENING";
    public const string DebitNote = "DEBIT_NOTE";
    public const string CreditNote = "CREDIT_NOTE";

    public static readonly IReadOnlyCollection<string> All = [Receipt, Payment, Contra, Journal, Opening, DebitNote, CreditNote];

    /// <summary>The Journal Entry voucher type ERPNext books an entry of this kind as.</summary>
    public static string ErpNextVoucherType(string kind) => kind switch
    {
        Receipt or Payment => "Bank Entry",
        Contra => "Contra Entry",
        Opening => "Opening Entry",
        DebitNote => "Debit Note",
        CreditNote => "Credit Note",
        _ => "Journal Entry"
    };
}
