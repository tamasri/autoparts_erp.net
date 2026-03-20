namespace AutoPartsERP.Application.Common.Abstractions.Markers;

public interface IPeriodSensitiveRequest
{
    DateTimeOffset OperationDate { get; }

    string Module { get; }
}
