using System.ComponentModel;

namespace AutoPartsERP.Domain.Operational.Enums;

public enum BatchStatus
{
    [Description("نشط")]
    Active,
    [Description("مستنفد")]
    Depleted,
    [Description("حجر صحي")]
    Quarantine,
    [Description("مُرجع")]
    Returned
}
