namespace AutoPartsERP.Application.Common.Abstractions.Markers;

public interface IAuditableRequest
{
    string AuditModule { get; }
}
