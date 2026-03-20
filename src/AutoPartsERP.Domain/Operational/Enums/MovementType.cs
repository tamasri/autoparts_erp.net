using System.ComponentModel;

namespace AutoPartsERP.Domain.Operational.Enums;

public enum MovementType
{
    [Description("استلام")]
    Receipt,
    [Description("صادر فاتورة")]
    InvoiceOut,
    [Description("إرجاع وارد")]
    ReturnIn,
    [Description("تحويل صادر")]
    TransferOut,
    [Description("تحويل وارد")]
    TransferIn,
    [Description("تسوية")]
    Adjustment,
    [Description("ضمان صادر")]
    WarrantyOut,
    [Description("ضمان وارد")]
    WarrantyIn,
    [Description("إتلاف")]
    WriteOff
}
