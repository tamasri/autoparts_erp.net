using System.ComponentModel;

namespace AutoPartsERP.Domain.Operational.Enums;

public enum WarrantyStatus
{
    [Description("ساري")]
    Active,
    [Description("مطالب به")]
    Claimed,
    [Description("منتهي")]
    Expired,
    [Description("مرفوض")]
    Rejected,
    [Description("ملغي")]
    Voided
}
