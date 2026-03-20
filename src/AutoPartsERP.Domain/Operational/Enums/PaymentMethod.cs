using System.ComponentModel;

namespace AutoPartsERP.Domain.Operational.Enums;

public enum PaymentMethod
{
    [Description("نقداً")]
    Cash,
    [Description("تحويل بنكي")]
    BankTransfer,
    [Description("شيك")]
    Cheque,
    [Description("دولار نقداً")]
    UsdCash
}
