using System.ComponentModel;

namespace AutoPartsERP.Domain.Operational.Enums;

public enum CustomerType
{
    [Description("ورشة")]
    Workshop,
    [Description("تجزئة")]
    Retail,
    [Description("جملة")]
    Wholesale
}
