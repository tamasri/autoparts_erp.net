using System.ComponentModel;

namespace AutoPartsERP.Domain.Operational.Enums;

public enum InvoiceStatus
{
    [Description("مسودة")]
    Draft,
    [Description("مؤكدة")]
    Confirmed,
    [Description("مرحّلة")]
    Posted,
    [Description("ملغاة")]
    Void,
    [Description("محذوفة")]
    Cancelled
}
