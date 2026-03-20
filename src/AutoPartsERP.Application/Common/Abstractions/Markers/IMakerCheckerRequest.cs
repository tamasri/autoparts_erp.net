namespace AutoPartsERP.Application.Common.Abstractions.Markers;

public interface IMakerCheckerRequest
{
    bool RequiresApproval { get; }
}
