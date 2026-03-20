using System.ComponentModel;

namespace AutoPartsERP.Domain.Operational.Enums;

public enum InvoiceType
{
    [Description("بيع")]
    Sale,
    [Description("إرجاع")]
    Return,
    [Description("إشعار دائن")]
    CreditNote
}
