namespace AutoPartsERP.Application.Common.Abstractions.Markers;

public interface IAuthorizedRequest
{
    string RequiredPermission { get; }
}
